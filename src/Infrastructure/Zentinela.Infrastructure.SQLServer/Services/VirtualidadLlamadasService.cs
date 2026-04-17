using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.LlamadasPerdidas;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class VirtualidadLlamadasService : IVirtualidadLlamadasService
{
    private readonly CallCenterDbContext _db;

    public VirtualidadLlamadasService(CallCenterDbContext db) => _db = db;

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<ValidarLlamadasResponse> ValidarExcelAsync(Stream excelStream)
    {
        var filas = ParsearExcel(excelStream);
        if (filas.Count == 0)
            return new ValidarLlamadasResponse();

        // Resolver CarteraID por NombreCartera
        var nombres = filas.Select(f => f.NombreCartera.Trim()).Distinct().ToList();
        var carteras = await _db.Carteras
            .Where(c => nombres.Contains(c.NombreCartera))
            .ToDictionaryAsync(c => c.NombreCartera, c => c.ID, StringComparer.OrdinalIgnoreCase);

        foreach (var f in filas.Where(f => f.CarteraID == 0))
        {
            if (carteras.TryGetValue(f.NombreCartera.Trim(), out var id))
                f.CarteraID = id;
            else
                f.Estado = "Cartera no encontrada — se descartará";
        }

        var conCartera = filas.Where(f => f.CarteraID > 0).ToList();

        // Validar vigencia: TitularID debe existir en Titular_Cartera
        var pares = conCartera
            .Select(f => new { f.TitularID, f.CarteraID })
            .Distinct()
            .ToList();

        var vigentes = await _db.TitularCarteras
            .Where(tc => pares.Select(p => p.TitularID).Contains(tc.TitularID)
                      && pares.Select(p => p.CarteraID).Contains(tc.CarteraID))
            .Select(tc => new { tc.TitularID, tc.CarteraID })
            .ToListAsync();

        var vigentesSet = vigentes
            .Select(v => (v.TitularID, v.CarteraID))
            .ToHashSet();

        foreach (var f in conCartera)
        {
            if (vigentesSet.Contains((f.TitularID, f.CarteraID)))
                f.Estado = "Válida";
            else
                f.Estado = "No vigente — se descartará";
        }

        var validas = filas.Where(f => f.Estado == "Válida").ToList();

        return new ValidarLlamadasResponse
        {
            TotalFilas     = filas.Count,
            TotalValidas   = validas.Count,
            TotalInvalidas = filas.Count - validas.Count,
            Preview        = filas.Take(10).ToList(),
            FilasValidas   = validas,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<int> ConfirmarAsync(List<FilaLlamadaDto> filas)
    {
        var entidades = filas.Select(f => new LlamadaPerdida
        {
            TitularID = f.TitularID,
            Celular   = f.Celular,
            CarteraID = f.CarteraID,
        }).ToList();

        _db.LlamadasPerdidas.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Informe ───────────────────────────────────────────────────────────────

    public async Task<InformeLlamadasDto> GetInformeAsync()
    {
        var porCartera = await _db.LlamadasPerdidas
            .Join(_db.Carteras, lp => lp.CarteraID, c => c.ID,
                  (lp, c) => new { c.NombreCartera })
            .GroupBy(x => x.NombreCartera)
            .Select(g => new LlamadasPorCartera { NombreCartera = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToListAsync();

        var alertas = await _db.LlamadasPerdidas
            .GroupBy(lp => lp.Celular)
            .Where(g => g.Count() > 1)
            .Select(g => new AlertaLlamadaDto
            {
                Celular         = g.Key,
                VecesComunicado = g.Count(),
            })
            .OrderByDescending(a => a.VecesComunicado)
            .ToListAsync();

        // Llamadas vs Gestión: titulares con llamadas perdidas que tienen datos en UltimoEstadoTitular
        var titularIDs = await _db.LlamadasPerdidas
            .Select(lp => lp.TitularID)
            .Distinct()
            .Take(200)
            .ToListAsync();

        var vsGestion = await _db.UltimosEstadosTitular
            .Where(u => titularIDs.Contains(u.TitularID))
            .Join(_db.LlamadasPerdidas, u => u.TitularID, lp => lp.TitularID,
                  (u, lp) => new LlamadaVsGestionDto
                  {
                      TitularID           = u.TitularID,
                      Celular             = lp.Celular,
                      UltimoPerfil        = u.UltimoPerfil,
                      FechaUltimaGestion  = u.FechaUltimaGestion,
                      FechaPromesa        = u.FechaPromesa,
                  })
            .Distinct()
            .Take(100)
            .ToListAsync();

        return new InformeLlamadasDto
        {
            TotalRegistros = porCartera.Sum(p => p.Total),
            PorCartera     = porCartera,
            Alertas        = alertas,
            VsGestion      = vsGestion,
        };
    }

    // ── Parseo Excel ──────────────────────────────────────────────────────────

    private static List<FilaLlamadaDto> ParsearExcel(Stream stream)
    {
        using var wb  = new XLWorkbook(stream);
        var ws        = wb.Worksheet(1);
        var resultado = new List<FilaLlamadaDto>();

        var todasFilas = ws.RowsUsed().ToList();
        if (todasFilas.Count < 2) return resultado;

        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var celda in todasFilas[0].CellsUsed())
        {
            var clave = Normalizar(celda.GetString());
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = celda.Address.ColumnNumber;
        }

        int Get(params string[] aliases)
        {
            foreach (var a in aliases) if (headers.TryGetValue(a, out var col)) return col;
            return 0;
        }

        int iTitular = Get("titularid", "titular", "numerodocumento", "cedula", "identificacion");
        int iCelular = Get("celular", "telefono", "numerotelefono");
        int iCartera = Get("nombrecartera", "cartera", "nombredecartera");

        foreach (var fila in todasFilas.Skip(1))
        {
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(Cel(iTitular))) continue;

            var dto = new FilaLlamadaDto { Estado = "Pendiente" };

            if (!long.TryParse(Cel(iTitular), out var tid))
            { dto.Estado = "TitularID inválido — se descartará"; resultado.Add(dto); continue; }

            dto.TitularID     = tid;
            dto.Celular       = Cel(iCelular);
            dto.NombreCartera = Cel(iCartera);

            resultado.Add(dto);
        }

        return resultado;
    }

    private static string Normalizar(string s)
    {
        var sinTildes = s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "").Replace("_", "").Replace("-", "")
            .Replace("*", "").Replace("(", "").Replace(")", "")
            .Trim();
    }
}

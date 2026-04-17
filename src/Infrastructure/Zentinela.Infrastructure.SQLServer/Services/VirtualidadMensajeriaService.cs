using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.Mensajeria;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class VirtualidadMensajeriaService : IVirtualidadMensajeriaService
{
    private readonly CallCenterDbContext _db;

    public VirtualidadMensajeriaService(CallCenterDbContext db) => _db = db;

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<ValidarMensajeriaResponse> ValidarExcelAsync(Stream excelStream)
    {
        var filas = ParsearExcel(excelStream);
        if (filas.Count == 0)
            return new ValidarMensajeriaResponse();

        // Resolver CarteraID por NombreCartera (batch)
        var nombres = filas.Select(f => f.NombreCartera.Trim()).Distinct().ToList();
        var carteras = await _db.Carteras
            .Where(c => nombres.Contains(c.NombreCartera))
            .Select(c => new { c.NombreCartera, c.ID })
            .ToDictionaryAsync(c => c.NombreCartera, c => c.ID, StringComparer.OrdinalIgnoreCase);

        foreach (var f in filas)
        {
            if (carteras.TryGetValue(f.NombreCartera.Trim(), out var id))
                f.CarteraID = id;
            else
            {
                f.EsValida = false;
                f.ErrorMensaje = $"Cartera '{f.NombreCartera}' no encontrada en el sistema.";
            }
        }

        var validas = filas.Where(f => f.EsValida).ToList();

        // Alertas: >4 mensajes a la misma línea en esta carga + histórico
        var telefonos = validas.Select(f => f.NumeroTelefono).Distinct().ToList();
        var historico = await _db.MensajeriaEnviadas
            .Where(m => telefonos.Contains(m.NumeroTelefono))
            .GroupBy(m => new { m.NumeroTelefono, m.NombreCartera })
            .Select(g => new { g.Key.NumeroTelefono, g.Key.NombreCartera, Total = g.Count() })
            .ToListAsync();

        var conteoActual = validas
            .GroupBy(f => new { f.NumeroTelefono, f.NombreCartera })
            .Select(g => new { g.Key.NumeroTelefono, g.Key.NombreCartera, Total = g.Count() })
            .ToList();

        var alertas = conteoActual
            .Select(c => new AlertaMensajeriaDto
            {
                NumeroTelefono = c.NumeroTelefono,
                NombreCartera  = c.NombreCartera,
                TotalMensajes  = c.Total + (historico
                    .FirstOrDefault(h => h.NumeroTelefono == c.NumeroTelefono && h.NombreCartera == c.NombreCartera)
                    ?.Total ?? 0),
            })
            .Where(a => a.TotalMensajes > 4)
            .ToList();

        return new ValidarMensajeriaResponse
        {
            TotalFilas   = filas.Count,
            TotalValidas = validas.Count,
            TotalErrores = filas.Count - validas.Count,
            Preview      = filas.Take(10).ToList(),
            FilasValidas = validas,
            Alertas      = alertas,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<int> ConfirmarAsync(List<FilaMensajeriaDto> filas)
    {
        var entidades = filas.Select(f => new MensajeriaEnviada
        {
            NumeroDocumento = f.NumeroDocumento,
            NumeroTelefono  = f.NumeroTelefono,
            NombreCartera   = f.NombreCartera,
            CarteraID       = f.CarteraID,
            FechaEnvio      = f.FechaEnvio,
        }).ToList();

        _db.MensajeriaEnviadas.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Informe ───────────────────────────────────────────────────────────────

    public async Task<InformeMensajeriaDto> GetInformeAsync(DateOnly desde, DateOnly hasta)
    {
        var query = _db.MensajeriaEnviadas
            .Where(m => m.FechaEnvio >= desde && m.FechaEnvio <= hasta);

        var porCartera = await query
            .GroupBy(m => m.NombreCartera)
            .Select(g => new MensajesPorCartera { NombreCartera = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToListAsync();

        var alertas = await query
            .GroupBy(m => new { m.NumeroTelefono, m.NombreCartera })
            .Where(g => g.Count() > 4)
            .Select(g => new AlertaMensajeriaDto
            {
                NumeroTelefono = g.Key.NumeroTelefono,
                NombreCartera  = g.Key.NombreCartera,
                TotalMensajes  = g.Count(),
            })
            .OrderByDescending(a => a.TotalMensajes)
            .ToListAsync();

        return new InformeMensajeriaDto
        {
            TotalMensajes = porCartera.Sum(p => p.Total),
            PorCartera    = porCartera,
            Alertas       = alertas,
        };
    }

    // ── Parseo Excel ──────────────────────────────────────────────────────────

    private static List<FilaMensajeriaDto> ParsearExcel(Stream stream)
    {
        using var wb  = new XLWorkbook(stream);
        var ws        = wb.Worksheet(1);
        var resultado = new List<FilaMensajeriaDto>();

        var todasFilas = ws.RowsUsed().ToList();
        if (todasFilas.Count < 2) return resultado;

        // Resolver índices por nombre de columna
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var celda in todasFilas[0].CellsUsed())
        {
            var clave = Normalizar(celda.GetString());
            if (!string.IsNullOrEmpty(clave) && !headers.ContainsKey(clave))
                headers[clave] = celda.Address.ColumnNumber;
        }

        int Get(params string[] aliases)
        {
            foreach (var a in aliases)
                if (headers.TryGetValue(a, out var col)) return col;
            return 0;
        }

        int iDoc   = Get("numerodocumento", "documento", "cedula", "identificacion", "titularid");
        int iTel   = Get("numerotelefono", "telefono", "celular");
        int iCart  = Get("nombrecartera", "cartera", "nombredecartera");
        int iFecha = Get("fechaenvio", "fecha", "fechadeenvio", "fechaenvio");

        foreach (var fila in todasFilas.Skip(1))
        {
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(Cel(iDoc)) && string.IsNullOrWhiteSpace(Cel(iTel)))
                continue;

            var dto = new FilaMensajeriaDto { EsValida = true };

            if (!long.TryParse(Cel(iDoc), out var doc))
            {
                dto.EsValida = false;
                dto.ErrorMensaje = "NumeroDocumento inválido.";
            }
            else dto.NumeroDocumento = doc;

            dto.NumeroTelefono = Cel(iTel);
            dto.NombreCartera  = Cel(iCart);

            if (!DateOnly.TryParse(Cel(iFecha), CultureInfo.InvariantCulture, out var fecha)
                && !DateOnly.TryParseExact(Cel(iFecha), "dd/MM/yyyy", null, DateTimeStyles.None, out fecha))
            {
                dto.EsValida = false;
                dto.ErrorMensaje ??= "FechaEnvío inválida.";
            }
            else dto.FechaEnvio = fecha;

            if (string.IsNullOrWhiteSpace(dto.NumeroTelefono)) { dto.EsValida = false; dto.ErrorMensaje ??= "NumeroTelefono vacío."; }
            if (string.IsNullOrWhiteSpace(dto.NombreCartera))  { dto.EsValida = false; dto.ErrorMensaje ??= "NombreCartera vacío."; }

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

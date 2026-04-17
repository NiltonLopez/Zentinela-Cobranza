using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.IVR;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class VirtualidadIVRService : IVirtualidadIVRService
{
    private readonly CallCenterDbContext _db;

    public VirtualidadIVRService(CallCenterDbContext db) => _db = db;

    // ── Pre-envío ─────────────────────────────────────────────────────────────

    public async Task<ValidarIVRResponse> ValidarPreEnvioAsync(Stream excelStream)
    {
        var filas = ParsearExcel(excelStream);

        // Resolver CarteraID si viene NombreCartera en el archivo
        var nombres = filas
            .Where(f => f.CarteraID == null && !string.IsNullOrWhiteSpace(f.Campana))
            .Select(f => f.Campana!.Trim()).Distinct().ToList();

        var validas = filas.Where(f => f.EsValida).ToList();

        return new ValidarIVRResponse
        {
            TotalFilas   = filas.Count,
            TotalValidas = validas.Count,
            TotalErrores = filas.Count - validas.Count,
            Preview      = filas.Take(10).ToList(),
            FilasValidas = validas,
        };
    }

    public async Task<int> ConfirmarPreEnvioAsync(List<FilaIVRDto> filas)
    {
        var entidades = filas.Select(f => new IVREnvio
        {
            TitularID  = f.TitularID,
            Celular    = f.Celular,
            Campana    = f.Campana,
            CarteraID  = f.CarteraID,
            FechaEnvio = f.FechaEnvio,
            Resultado  = null,
        }).ToList();

        _db.IVREnvios.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Post-envío ────────────────────────────────────────────────────────────

    public async Task<ValidarIVRResponse> ValidarPostEnvioAsync(Stream excelStream)
    {
        var filas = ParsearExcel(excelStream);
        var validas = filas.Where(f => f.EsValida && !string.IsNullOrWhiteSpace(f.Resultado)).ToList();

        // Verificar que exista el pre-envío correspondiente
        foreach (var f in validas)
        {
            var existe = await _db.IVREnvios.AnyAsync(i =>
                i.TitularID == f.TitularID &&
                i.Celular   == f.Celular   &&
                i.Campana   == f.Campana   &&
                i.Resultado == null);

            if (!existe)
            {
                f.EsValida = false;
                f.ErrorMensaje = "No se encontró pre-envío pendiente para TitularID+Celular+Campaña.";
            }
        }

        var finales = validas.Where(f => f.EsValida).ToList();

        return new ValidarIVRResponse
        {
            TotalFilas   = filas.Count,
            TotalValidas = finales.Count,
            TotalErrores = filas.Count - finales.Count,
            Preview      = filas.Take(10).ToList(),
            FilasValidas = finales,
        };
    }

    public async Task<int> ConfirmarPostEnvioAsync(List<FilaIVRDto> filas)
    {
        var actualizados = 0;
        var ahora = DateTime.UtcNow;

        foreach (var f in filas)
        {
            var registro = await _db.IVREnvios
                .Where(i =>
                    i.TitularID == f.TitularID &&
                    i.Celular   == f.Celular   &&
                    i.Campana   == f.Campana   &&
                    i.Resultado == null)
                .FirstOrDefaultAsync();

            if (registro == null) continue;

            registro.Resultado      = f.Resultado;
            registro.FechaResultado = ahora;
            actualizados++;
        }

        await _db.SaveChangesAsync();
        return actualizados;
    }

    // ── Informe ───────────────────────────────────────────────────────────────

    public async Task<InformeIVRDto> GetInformeAsync(int? carteraID, DateOnly desde, DateOnly hasta)
    {
        var query = _db.IVREnvios
            .Where(i => i.FechaEnvio >= desde && i.FechaEnvio <= hasta);

        if (carteraID.HasValue)
            query = query.Where(i => i.CarteraID == carteraID.Value);

        var total       = await query.CountAsync();
        var contestaron = await query.CountAsync(i => i.Resultado != null && i.Resultado != "No Contesto");
        var noContesto  = await query.CountAsync(i => i.Resultado == "No Contesto");

        var porSemana = await query
            .GroupBy(i => new
            {
                Anio   = i.FechaEnvio.Year,
                Semana = EF.Functions.DateDiffDay(new DateOnly(i.FechaEnvio.Year, 1, 1), i.FechaEnvio) / 7 + 1,
            })
            .Select(g => new IVRPorSemana
            {
                Anio         = g.Key.Anio,
                Semana       = g.Key.Semana,
                Total        = g.Count(),
                Contestaron  = g.Count(i => i.Resultado != null && i.Resultado != "No Contesto"),
                NoContestaron= g.Count(i => i.Resultado == "No Contesto"),
            })
            .OrderBy(s => s.Anio).ThenBy(s => s.Semana)
            .ToListAsync();

        var alertas = await query
            .GroupBy(i => new { i.Celular, i.CarteraID })
            .Where(g => g.Count() > 5)
            .Select(g => new AlertaIVRDto
            {
                Celular   = g.Key.Celular,
                CarteraID = g.Key.CarteraID,
                TotalIVR  = g.Count(),
            })
            .ToListAsync();

        // Enriquecer alertas con nombre de cartera
        var carteraIDs = alertas.Where(a => a.CarteraID.HasValue).Select(a => a.CarteraID!.Value).Distinct().ToList();
        var nombresCarteras = await _db.Carteras
            .Where(c => carteraIDs.Contains(c.ID))
            .ToDictionaryAsync(c => c.ID, c => c.NombreCartera);
        foreach (var a in alertas)
            if (a.CarteraID.HasValue && nombresCarteras.TryGetValue(a.CarteraID.Value, out var nombre))
                a.NombreCartera = nombre;

        // IVR contestados vs gestión en UltimoEstadoTitular
        var idsContestaron = await query
            .Where(i => i.Resultado != null && i.Resultado != "No Contesto")
            .Select(i => new { i.TitularID, i.Celular, i.Resultado, i.FechaEnvio })
            .Take(200)
            .ToListAsync();

        var titularIDs = idsContestaron.Select(i => i.TitularID).Distinct().ToList();
        var estadosTitulares = await _db.UltimosEstadosTitular
            .Where(u => titularIDs.Contains(u.TitularID))
            .Select(u => new { u.TitularID, u.UltimoPerfil, u.FechaUltimaGestion, u.FechaPromesa })
            .ToListAsync();

        var estadoMap = estadosTitulares.ToDictionary(u => u.TitularID);

        var vsGestion = idsContestaron.Select(i =>
        {
            estadoMap.TryGetValue(i.TitularID, out var est);
            return new IVRVsGestionDto
            {
                TitularID          = i.TitularID,
                Celular            = i.Celular,
                Resultado          = i.Resultado,
                UltimoPerfil       = est?.UltimoPerfil,
                FechaUltimaGestion = est?.FechaUltimaGestion,
                FechaPromesa       = est?.FechaPromesa,
                FechaEnvioIVR      = i.FechaEnvio,
            };
        }).ToList();

        return new InformeIVRDto
        {
            TotalSolicitados   = total,
            TotalContestaron   = contestaron,
            TotalNoContestaron = noContesto,
            PctContesto        = total > 0 ? Math.Round(contestaron * 100.0 / total, 1) : 0,
            PctNoContesto      = total > 0 ? Math.Round(noContesto * 100.0 / total, 1) : 0,
            PorSemana          = porSemana,
            Alertas            = alertas,
            VsGestion          = vsGestion,
        };
    }

    // ── Parseo Excel ──────────────────────────────────────────────────────────

    private static List<FilaIVRDto> ParsearExcel(Stream stream)
    {
        using var wb  = new XLWorkbook(stream);
        var ws        = wb.Worksheet(1);
        var resultado = new List<FilaIVRDto>();

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

        int iTitular  = Get("titularid", "titular", "numerodocumento", "cedula", "identificacion");
        int iCelular  = Get("celular", "telefono", "numerotelefono");
        int iCampana  = Get("campana", "campaign", "nombre");
        int iCartera  = Get("carteraid", "cartera");
        int iFecha    = Get("fechaenvio", "fecha");
        int iResultado= Get("resultado", "result");

        foreach (var fila in todasFilas.Skip(1))
        {
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(Cel(iTitular)) && string.IsNullOrWhiteSpace(Cel(iCelular)))
                continue;

            var dto = new FilaIVRDto { EsValida = true };

            if (!long.TryParse(Cel(iTitular), out var tid))
            { dto.EsValida = false; dto.ErrorMensaje = "TitularID inválido."; }
            else dto.TitularID = tid;

            dto.Celular   = Cel(iCelular);
            dto.Campana   = string.IsNullOrWhiteSpace(Cel(iCampana)) ? null : Cel(iCampana);
            dto.Resultado = string.IsNullOrWhiteSpace(Cel(iResultado)) ? null : Cel(iResultado);

            if (int.TryParse(Cel(iCartera), out var cid)) dto.CarteraID = cid;

            if (!DateOnly.TryParse(Cel(iFecha), CultureInfo.InvariantCulture, out var fecha)
                && !DateOnly.TryParseExact(Cel(iFecha), "dd/MM/yyyy", null, DateTimeStyles.None, out fecha))
            {
                // Fecha opcional — usar hoy si no viene
                fecha = DateOnly.FromDateTime(DateTime.Today);
            }
            dto.FechaEnvio = fecha;

            if (string.IsNullOrWhiteSpace(dto.Celular)) { dto.EsValida = false; dto.ErrorMensaje ??= "Celular vacío."; }

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

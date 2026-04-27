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

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("IVR");

        var columnas = new[] { "NumeroDocumento", "NumeroTelefono", "FechaEnvio" };

        for (int i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0891b2");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var ej = new object[] { 1020304050L, "3001234567", "2026-04-24" };
        for (int i = 0; i < ej.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = ej[i].ToString();
            c.Style.Font.FontColor = XLColor.Gray;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<ValidarIVRResponse> ValidarExcelAsync(Stream excelStream, int carteraID)
    {
        var cartera = await _db.Carteras
            .Where(c => c.ID == carteraID)
            .Select(c => new { c.ID, c.NombreCartera })
            .FirstOrDefaultAsync();

        if (cartera is null)
            return new ValidarIVRResponse
            {
                TotalErrores = 1,
                Preview = [new FilaIVRDto
                {
                    EsValida = false,
                    ErrorMensaje = $"La cartera seleccionada (ID {carteraID}) no existe en el sistema.",
                }],
            };

        var filas = ParsearExcel(excelStream);
        if (filas.Count == 0)
            return new ValidarIVRResponse();

        foreach (var f in filas)
        {
            f.CarteraID    = cartera.ID;
            f.NombreCartera = cartera.NombreCartera;
        }

        var validas = filas.Where(f => f.EsValida).ToList();

        // Alertas: >5 IVR a la misma línea en esta carga + histórico de la cartera
        var telefonos = validas.Select(f => f.NumeroTelefono).Distinct().ToList();
        var historico = await _db.IVREnviados
            .Where(i => i.CarteraID == carteraID && telefonos.Contains(i.NumeroTelefono))
            .GroupBy(i => i.NumeroTelefono)
            .Select(g => new { NumeroTelefono = g.Key, Total = g.Count() })
            .ToListAsync();

        var conteoActual = validas
            .GroupBy(f => f.NumeroTelefono)
            .Select(g => new { NumeroTelefono = g.Key, Total = g.Count() })
            .ToList();

        var alertas = conteoActual
            .Select(c => new AlertaIVRDto
            {
                NumeroTelefono = c.NumeroTelefono,
                NombreCartera  = cartera.NombreCartera,
                TotalIVR       = c.Total + (historico
                    .FirstOrDefault(h => h.NumeroTelefono == c.NumeroTelefono)
                    ?.Total ?? 0),
            })
            .Where(a => a.TotalIVR > 5)
            .ToList();

        return new ValidarIVRResponse
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

    public async Task<int> ConfirmarAsync(List<FilaIVRDto> filas)
    {
        var entidades = filas.Select(f => new IVREnviado
        {
            NumeroDocumento = f.NumeroDocumento,
            NumeroTelefono  = f.NumeroTelefono,
            LiderID         = f.LiderID,
            CarteraID       = f.CarteraID,
            FechaEnvio      = f.FechaEnvio,
            Canal           = "IVR",
        }).ToList();

        _db.IVREnviados.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Informe ───────────────────────────────────────────────────────────────

    public async Task<InformeIVRDto> GetInformeAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        var queryEnriquecida =
            from i in _db.IVREnviados
            where i.FechaEnvio >= desde && i.FechaEnvio <= hasta
                && (!liderID.HasValue   || i.LiderID   == liderID.Value)
                && (!carteraID.HasValue || i.CarteraID == carteraID.Value)
            join c in _db.Carteras on i.CarteraID equals c.ID
            join l in _db.Lideres  on i.LiderID   equals l.ID
            select new
            {
                i.LiderID,
                NombreLider   = l.Nombre,
                i.CarteraID,
                c.NombreCartera,
                i.NumeroTelefono,
            };

        var porLiderCartera = await queryEnriquecida
            .GroupBy(x => new { x.LiderID, x.NombreLider, x.CarteraID, x.NombreCartera })
            .Select(g => new IVRPorLiderCartera
            {
                LiderID       = g.Key.LiderID,
                NombreLider   = g.Key.NombreLider,
                CarteraID     = g.Key.CarteraID,
                NombreCartera = g.Key.NombreCartera,
                Total         = g.Count(),
            })
            .OrderByDescending(g => g.Total)
            .ToListAsync();

        var alertas = await queryEnriquecida
            .GroupBy(x => new { x.NumeroTelefono, x.NombreCartera })
            .Where(g => g.Count() > 5)
            .Select(g => new AlertaIVRDto
            {
                NumeroTelefono = g.Key.NumeroTelefono,
                NombreCartera  = g.Key.NombreCartera,
                TotalIVR       = g.Count(),
            })
            .OrderByDescending(a => a.TotalIVR)
            .ToListAsync();

        return new InformeIVRDto
        {
            TotalIVR        = porLiderCartera.Sum(p => p.Total),
            PorLiderCartera = porLiderCartera,
            Alertas         = alertas,
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
            foreach (var a in aliases)
                if (headers.TryGetValue(a, out var col)) return col;
            return 0;
        }

        int iDoc   = Get("numerodocumento", "documento", "cedula", "identificacion", "titularid");
        int iTel   = Get("numerotelefono", "telefono", "celular");
        int iFecha = Get("fechaenvio", "fecha", "fechadeenvio");

        foreach (var fila in todasFilas.Skip(1))
        {
            string Cel(int idx) => idx > 0 ? fila.Cell(idx).GetString().Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(Cel(iDoc)) && string.IsNullOrWhiteSpace(Cel(iTel)))
                continue;

            var dto = new FilaIVRDto { EsValida = true };

            if (!long.TryParse(Cel(iDoc), out var doc))
            {
                dto.EsValida = false;
                dto.ErrorMensaje = "NumeroDocumento inválido.";
            }
            else dto.NumeroDocumento = doc;

            dto.NumeroTelefono = Cel(iTel);

            if (!DateOnly.TryParse(Cel(iFecha), CultureInfo.InvariantCulture, out var fecha)
                && !DateOnly.TryParseExact(Cel(iFecha), "dd/MM/yyyy", null, DateTimeStyles.None, out fecha))
            {
                dto.EsValida = false;
                dto.ErrorMensaje ??= "FechaEnvío inválida.";
            }
            else dto.FechaEnvio = fecha;

            if (string.IsNullOrWhiteSpace(dto.NumeroTelefono)) { dto.EsValida = false; dto.ErrorMensaje ??= "NumeroTelefono vacío."; }

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

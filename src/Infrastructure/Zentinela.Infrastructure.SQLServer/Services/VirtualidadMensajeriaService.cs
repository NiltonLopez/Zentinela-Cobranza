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

    // ── Plantilla ─────────────────────────────────────────────────────────────

    public byte[] GenerarPlantilla()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Mensajería");

        // La cartera ya no va en el archivo — se selecciona en la UI
        var columnas = new[] { "NumeroDocumento", "NumeroTelefono", "FechaEnvio" };

        for (int i = 0; i < columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = columnas[i];
            c.Style.Font.Bold            = true;
            c.Style.Font.FontColor       = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed");
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

    public async Task<ValidarMensajeriaResponse> ValidarExcelAsync(Stream excelStream, int carteraID)
    {
        // Resolver nombre de cartera desde el ID seleccionado en la UI
        var cartera = await _db.Carteras
            .Where(c => c.ID == carteraID)
            .Select(c => new { c.ID, c.NombreCartera })
            .FirstOrDefaultAsync();

        if (cartera is null)
            return new ValidarMensajeriaResponse
            {
                TotalErrores = 1,
                Preview = [new FilaMensajeriaDto
                {
                    EsValida = false,
                    ErrorMensaje = $"La cartera seleccionada (ID {carteraID}) no existe en el sistema.",
                }],
            };

        var filas = ParsearExcel(excelStream);
        if (filas.Count == 0)
            return new ValidarMensajeriaResponse();

        // Inyectar cartera en todas las filas (viene del selector de la UI)
        foreach (var f in filas)
        {
            f.CarteraID    = cartera.ID;
            f.NombreCartera = cartera.NombreCartera;
        }

        var validas = filas.Where(f => f.EsValida).ToList();

        // Alertas: >4 mensajes a la misma línea en esta carga + histórico de la cartera
        var telefonos = validas.Select(f => f.NumeroTelefono).Distinct().ToList();
        var historico = await _db.MensajeriaEnviadas
            .Where(m => m.CarteraID == carteraID && telefonos.Contains(m.NumeroTelefono))
            .GroupBy(m => m.NumeroTelefono)
            .Select(g => new { NumeroTelefono = g.Key, Total = g.Count() })
            .ToListAsync();

        var conteoActual = validas
            .GroupBy(f => f.NumeroTelefono)
            .Select(g => new { NumeroTelefono = g.Key, Total = g.Count() })
            .ToList();

        var alertas = conteoActual
            .Select(c => new AlertaMensajeriaDto
            {
                NumeroTelefono = c.NumeroTelefono,
                NombreCartera  = cartera.NombreCartera,
                TotalMensajes  = c.Total + (historico
                    .FirstOrDefault(h => h.NumeroTelefono == c.NumeroTelefono)
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
            CarteraID       = f.CarteraID,
            NombreCartera   = f.NombreCartera,
            FechaEnvio      = f.FechaEnvio,
            Canal           = "SMS",
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

        // NombreCartera ya no va en el archivo — se inyecta desde el selector de la UI
        int iDoc   = Get("numerodocumento", "documento", "cedula", "identificacion", "titularid");
        int iTel   = Get("numerotelefono", "telefono", "celular");
        int iFecha = Get("fechaenvio", "fecha", "fechadeenvio");

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

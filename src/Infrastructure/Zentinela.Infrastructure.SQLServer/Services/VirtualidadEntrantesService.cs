using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Virtualidad.LlamadasEntrantes;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

public class VirtualidadEntrantesService : IVirtualidadEntrantesService
{
    private readonly CallCenterDbContext _db;

    public VirtualidadEntrantesService(CallCenterDbContext db) => _db = db;

    // ── Validación ────────────────────────────────────────────────────────────

    public async Task<ValidarEntrantesResponse> ValidarExcelAsync(Stream excelStream)
    {
        var celulares = ParsearCelulares(excelStream);
        if (celulares.Count == 0)
            return new ValidarEntrantesResponse();

        var hoy    = DateTime.Today;
        var semana = SemanaISO(hoy);
        var anio   = hoy.Year;

        // Resolver TitularID + CarteraID desde NumeroTelefonoGeneral (raw SQL — tabla sin entidad EF)
        var telefonosDistintos = celulares.Distinct().ToList();

        // NumeroTelefonoGeneral: TitularID varchar(50), NumeroTelefono varchar(10)
        var resoluciones = await _db.Database
            .SqlQueryRaw<TelefonoResolucion>(
                "SELECT TitularID, NumeroTelefono AS Celular FROM NumeroTelefonoGeneral WHERE NumeroTelefono IN ({0})",
                string.Join(",", telefonosDistintos.Select(t => $"'{t}'")))
            .ToListAsync();

        var resolMap = resoluciones
            .Where(r => long.TryParse(r.TitularID, out _))
            .GroupBy(r => r.Celular)
            .ToDictionary(g => g.Key, g => long.Parse(g.First().TitularID));

        // Resolver CarteraID desde TitularCartera para titulares resueltos
        var titularIDs = resolMap.Values.Distinct().ToList();
        var carterasMap = await _db.TitularCarteras
            .Where(tc => titularIDs.Contains(tc.TitularID))
            .GroupBy(tc => tc.TitularID)
            .Select(g => new { TitularID = g.Key, CarteraID = g.Min(tc => tc.CarteraID) })
            .ToDictionaryAsync(x => x.TitularID, x => x.CarteraID);

        // Verificar acuerdo de pago en UltimoEstadoTitular
        var conAcuerdo = await _db.UltimosEstadosTitular
            .Where(u => titularIDs.Contains(u.TitularID) && u.FechaPromesa != null)
            .Select(u => u.TitularID)
            .ToHashSetAsync();

        var filas = celulares.Distinct().Select(cel =>
        {
            resolMap.TryGetValue(cel, out var tid);
            carterasMap.TryGetValue(tid, out var cid);
            return new FilaEntranteDto
            {
                Celular      = cel,
                TitularID    = tid == 0 ? null : tid,
                CarteraID    = cid == 0 ? null : cid,
                TieneAcuerdo = tid > 0 ? conAcuerdo.Contains(tid) : null,
                Semana       = semana,
                Anio         = anio,
            };
        }).ToList();

        var resueltos  = filas.Count(f => f.TitularID.HasValue);
        var conAcuerdoN= filas.Count(f => f.TieneAcuerdo == true);

        return new ValidarEntrantesResponse
        {
            TotalCelulares   = filas.Count,
            TotalResueltos   = resueltos,
            TotalConAcuerdo  = conAcuerdoN,
            Efectividad      = resueltos > 0 ? Math.Round(conAcuerdoN * 100.0 / resueltos, 1) : 0,
            Preview          = filas.Take(10).ToList(),
            FilasParaGuardar = filas,
        };
    }

    // ── Confirmación ──────────────────────────────────────────────────────────

    public async Task<int> ConfirmarAsync(List<FilaEntranteDto> filas)
    {
        var entidades = filas.Select(f => new LlamadaEntrante
        {
            Celular      = f.Celular,
            TitularID    = f.TitularID,
            CarteraID    = f.CarteraID,
            TieneAcuerdo = f.TieneAcuerdo,
            Semana       = f.Semana,
            Anio         = f.Anio,
        }).ToList();

        _db.LlamadasEntrantes.AddRange(entidades);
        await _db.SaveChangesAsync();
        return entidades.Count;
    }

    // ── Informe ───────────────────────────────────────────────────────────────

    public async Task<InformeEntrantesDto> GetInformeAsync(int anio)
    {
        var porSemana = await _db.LlamadasEntrantes
            .Where(l => l.Anio == anio)
            .GroupBy(l => new { l.Anio, l.Semana })
            .Select(g => new EfectividadSemanal
            {
                Anio           = g.Key.Anio,
                Semana         = g.Key.Semana,
                TotalCelulares = g.Count(),
                Resueltos      = g.Count(l => l.TitularID != null),
                ConAcuerdo     = g.Count(l => l.TieneAcuerdo == true),
                Efectividad    = g.Count(l => l.TitularID != null) > 0
                    ? Math.Round(g.Count(l => l.TieneAcuerdo == true) * 100.0 / g.Count(l => l.TitularID != null), 1)
                    : 0,
            })
            .OrderBy(s => s.Semana)
            .ToListAsync();

        return new InformeEntrantesDto { PorSemana = porSemana };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> ParsearCelulares(Stream stream)
    {
        using var wb  = new XLWorkbook(stream);
        var ws        = wb.Worksheet(1);
        var resultado = new List<string>();

        var filas = ws.RowsUsed().ToList();
        // Detectar si la primera fila es encabezado o dato
        var inicio = filas.Count > 0 && !long.TryParse(filas[0].Cell(1).GetString().Trim(), out _) ? 1 : 0;

        foreach (var fila in filas.Skip(inicio))
        {
            var cel = fila.Cell(1).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(cel))
                resultado.Add(cel);
        }

        return resultado;
    }

    private static int SemanaISO(DateTime fecha)
    {
        var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
        return cal.GetWeekOfYear(fecha,
            System.Globalization.CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);
    }

    // Tipo auxiliar para el raw SQL query de NumeroTelefonoGeneral
    private record TelefonoResolucion(string TitularID, string Celular);
}

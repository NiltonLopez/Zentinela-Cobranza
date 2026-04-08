using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Dashboard;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Construye el dashboard del director consultando las 3 bases de datos:
/// - DBCallCenter: HistoricoEstadoTitular, UltimoEstadoTitular, LiderCartera, Cartera, Lider
/// - DBRecursosHumanos: Empleado (para nombres de asesores)
/// </summary>
public class DashboardDirectorService : IDashboardDirectorService
{
    private readonly CallCenterDbContext      _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;

    public DashboardDirectorService(
        CallCenterDbContext callCenter,
        RecursosHumanosDbContext rrhh)
    {
        _callCenter = callCenter;
        _rrhh = rrhh;
    }

    // ── Dropdowns ─────────────────────────────────────────────────────────────

    public async Task<List<LiderDropdownDto>> ObtenerLideresAsync()
    {
        return await _callCenter.Lideres
            .Where(l => l.Activo)
            .OrderBy(l => l.Nombre)
            .Select(l => new LiderDropdownDto { ID = l.ID, Nombre = l.Nombre })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<CarteraDropdownDto>> ObtenerCarterasLiderAsync(int liderID)
    {
        return await _callCenter.LiderCarteras
            .Where(lc => lc.LiderID == liderID)
            .Select(lc => new CarteraDropdownDto
            {
                ID = lc.CartID,
                NombreCartera = lc.Cartera.NombreCartera,
            })
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Dashboard principal ───────────────────────────────────────────────────

    public async Task<DashboardDirectorDto> ObtenerDashboardAsync(int? liderID, int? carteraID)
    {
        var fechaActualizacion = await ObtenerFechaActualizacionAsync(liderID, carteraID);
        if (fechaActualizacion is null)
            return new DashboardDirectorDto { Metricas = new() };

        var fechaReporte = DateOnly.FromDateTime(fechaActualizacion.Value);

        // Construir query base filtrando por fecha del ciclo
        IQueryable<Zentinela.Core.Entities.HistoricoEstadoTitular> query =
            _callCenter.HistoricoEstadosTitular
                .Where(h => h.FechaUltimaGestion == fechaReporte);

        if (carteraID.HasValue)
        {
            query = query.Where(h => h.CarteraID == carteraID.Value);
        }
        else if (liderID.HasValue)
        {
            var carteraIDs = await _callCenter.LiderCarteras
                .Where(lc => lc.LiderID == liderID.Value)
                .Select(lc => lc.CartID)
                .ToListAsync();

            query = query.Where(h => carteraIDs.Contains(h.CarteraID));
        }

        var registros = await query
            .Select(h => new { h.CarteraID, h.AgenteID, h.UltimoPerfil })
            .AsNoTracking()
            .ToListAsync();

        // ── Métricas globales ─────────────────────────────────────────────────
        var totalGestiones     = registros.Count;
        var contactosPositivos = registros.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
        var acuerdosAbonos     = registros.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));

        var metricas = new MetricasDirectorDto
        {
            FechaActualizacion  = fechaActualizacion,
            TotalGestiones      = totalGestiones,
            TasaContactabilidad = totalGestiones > 0
                ? Math.Round((decimal)contactosPositivos / totalGestiones, 4) : 0,
            TasaEfectividad     = contactosPositivos > 0
                ? Math.Round((decimal)acuerdosAbonos / contactosPositivos, 4) : 0,
        };

        // ── Desglose ─────────────────────────────────────────────────────────
        List<FilaLiderDto>  filasLider  = [];
        List<FilaAsesorDto> filasAsesor = [];

        if (!liderID.HasValue)
        {
            filasLider = await ConstruirFilasLiderAsync(registros
                .Select(r => (r.CarteraID, r.AgenteID, r.UltimoPerfil))
                .ToList());
        }
        else
        {
            filasAsesor = await ConstruirFilasAsesorAsync(registros
                .Select(r => (r.AgenteID, r.UltimoPerfil))
                .ToList());
        }

        return new DashboardDirectorDto
        {
            Metricas    = metricas,
            FilasLider  = filasLider,
            FilasAsesor = filasAsesor,
        };
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private async Task<DateTime?> ObtenerFechaActualizacionAsync(int? liderID, int? carteraID)
    {
        IQueryable<Zentinela.Core.Entities.UltimoEstadoTitular> query =
            _callCenter.UltimosEstadosTitular;

        if (carteraID.HasValue)
        {
            query = query.Where(u => u.CarteraID == carteraID.Value);
        }
        else if (liderID.HasValue)
        {
            var carteraIDs = await _callCenter.LiderCarteras
                .Where(lc => lc.LiderID == liderID.Value)
                .Select(lc => lc.CartID)
                .ToListAsync();

            query = query.Where(u => carteraIDs.Contains(u.CarteraID));
        }

        return await query.MaxAsync(u => (DateTime?)u.FechaActualizacion);
    }

    private async Task<List<FilaLiderDto>> ConstruirFilasLiderAsync(
        List<(int CarteraID, int AgenteID, string? UltimoPerfil)> registros)
    {
        // Mapear CarteraID → LiderID usando LiderCartera
        var liderCarteras = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .AsNoTracking()
            .ToListAsync();

        // Si una cartera pertenece a varios líderes tomamos el primero registrado
        var carteraALider = liderCarteras
            .GroupBy(lc => lc.CartID)
            .ToDictionary(g => g.Key, g => g.First());

        var porLider = registros
            .Where(r => carteraALider.ContainsKey(r.CarteraID))
            .GroupBy(r => carteraALider[r.CarteraID].LiderID)
            .ToList();

        return porLider
            .Select(grp =>
            {
                var lc      = carteraALider.Values.First(x => x.LiderID == grp.Key);
                var total   = grp.Count();
                var contactos = grp.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
                var acuerdos  = grp.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));

                return new FilaLiderDto
                {
                    LiderID             = grp.Key,
                    NombreLider         = lc.Lider.Nombre,
                    TotalGestiones      = total,
                    TasaContactabilidad = total > 0
                        ? Math.Round((decimal)contactos / total, 4) : 0,
                    TasaEfectividad     = contactos > 0
                        ? Math.Round((decimal)acuerdos / contactos, 4) : 0,
                };
            })
            .OrderByDescending(f => f.TotalGestiones)
            .ToList();
    }

    private async Task<List<FilaAsesorDto>> ConstruirFilasAsesorAsync(
        List<(int AgenteID, string? UltimoPerfil)> registros)
    {
        // Obtener nombres desde DBRecursosHumanos
        var agenteIDs = registros.Select(r => (long)r.AgenteID).Distinct().ToList();

        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToListAsync();

        var nombrePorAgente = empleados.ToDictionary(
            e => (int)e.NumeroDocumento,
            e => $"{e.Nombre} {e.Apellidos}".Trim());

        return registros
            .GroupBy(r => r.AgenteID)
            .Select(grp =>
            {
                var total    = grp.Count();
                var contactos = grp.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
                var acuerdos  = grp.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));

                nombrePorAgente.TryGetValue(grp.Key, out var nombre);

                return new FilaAsesorDto
                {
                    AgenteID            = grp.Key,
                    NombreAsesor        = nombre ?? $"Agente {grp.Key}",
                    TotalGestiones      = total,
                    TasaContactabilidad = total > 0
                        ? Math.Round((decimal)contactos / total, 4) : 0,
                    TasaEfectividad     = contactos > 0
                        ? Math.Round((decimal)acuerdos / contactos, 4) : 0,
                };
            })
            .OrderByDescending(f => f.TotalGestiones)
            .ToList();
    }
}

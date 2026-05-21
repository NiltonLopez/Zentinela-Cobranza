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

    /// <summary>
    /// Líderes activos con al menos una relación interna (Lider_Cartera.EsExterna = 0).
    /// Excluye líderes externos puros (todas sus relaciones son externas).
    /// </summary>
    public async Task<List<LiderDropdownDto>> ObtenerLideresAsync()
    {
        return await _callCenter.Lideres
            .Where(l => l.Activo
                     && _callCenter.LiderCarteras.Any(lc => lc.LiderID == l.ID && !lc.EsExterna))
            .OrderBy(l => l.Nombre)
            .Select(l => new LiderDropdownDto { ID = l.ID, Nombre = l.Nombre })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<CarteraDropdownDto>> ObtenerCarterasLiderAsync(int liderID)
    {
        return await _callCenter.LiderCarteras
            .Where(lc => lc.LiderID == liderID && !lc.EsExterna)
            .Select(lc => new CarteraDropdownDto
            {
                ID = lc.CarteraID,
                NombreCartera = lc.Cartera.NombreCartera,
            })
            .OrderBy(c => c.NombreCartera)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Dashboard principal ───────────────────────────────────────────────────

    public async Task<DashboardDirectorDto> ObtenerDashboardAsync(
        int? liderID, int? carteraID, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        // FechaActualizacion es informativa (cuándo corrió el ETL)
        var fechaActualizacion = await ObtenerFechaActualizacionAsync(liderID, carteraID);

        // Sin fechas explícitas → usar el último día con gestiones registradas
        DateOnly desde, hasta;
        if (fechaDesde is null || fechaHasta is null)
        {
            var ultimaGestion = await ObtenerMaxFechaGestionAsync(liderID, carteraID);
            if (ultimaGestion is null)
                return new DashboardDirectorDto { Metricas = new() };

            desde = ultimaGestion.Value;
            hasta = ultimaGestion.Value;
        }
        else
        {
            desde = fechaDesde.Value;
            hasta = fechaHasta.Value;
        }

        // Fuente: UltimoEstadoTitular (HistoricoEstadoTitular aún no está poblada).
        // Regla de filtrado para Dashboard de Líder / Director:
        //  - Si hay liderID: incluir TODOS los estados gestionados por los asesores
        //    del líder, sin restringir cartera (los asesores pueden apoyar carteras
        //    de otros líderes y esas gestiones cuentan para su rendimiento).
        //  - Si solo hay carteraID: vista por cartera específica (típico del Director).
        //  - Si no hay filtros: vista global, excluyendo carteras solo externas.
        var query = _callCenter.UltimosEstadosTitular
            .Where(u => u.FechaUltimaGestion >= desde && u.FechaUltimaGestion <= hasta);

        if (carteraID.HasValue)
        {
            query = query.Where(u => u.CarteraID == carteraID.Value);
        }

        if (liderID.HasValue)
        {
            var lid = liderID.Value;
            query = query.Where(u => _callCenter.CarteraAgentes.Any(ca =>
                ca.AgenteID == u.AgenteID && ca.LiderID == lid));
        }
        else if (!carteraID.HasValue)
        {
            // Vista global del Director: excluir carteras exclusivamente externas.
            query = query.Where(u => _callCenter.LiderCarteras.Any(lc =>
                lc.CarteraID == u.CarteraID && !lc.EsExterna));
        }

        // Total de titulares asignados (sin filtro de fecha — es el universo completo del filtro)
        var totalAsignados = await ContarAsignadosAsync(liderID, carteraID);

        var registros = await query
            .Select(u => new { u.CarteraID, u.AgenteID, u.UltimoPerfil, u.ValorPromesa })
            .AsNoTracking()
            .ToListAsync();

        // ── Métricas globales ─────────────────────────────────────────────────
        var totalGestiones     = registros.Count;
        var contactosPositivos = registros.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
        var acuerdosAbonos     = registros.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));
        var proyeccionPromesas = registros
            .Where(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty))
            .Sum(r => r.ValorPromesa ?? 0m);
        var totalIncumplidos   = registros.Count(r =>
            string.Equals(r.UltimoPerfil, "Incumplido", StringComparison.OrdinalIgnoreCase));

        var metricas = new MetricasDirectorDto
        {
            FechaActualizacion  = fechaActualizacion,
            FechaDesde          = desde,
            FechaHasta          = hasta,
            TotalGestiones      = totalGestiones,
            TasaContactabilidad = totalGestiones > 0
                ? Math.Round((decimal)contactosPositivos / totalGestiones, 4) : 0,
            TasaEfectividad     = contactosPositivos > 0
                ? Math.Round((decimal)acuerdosAbonos / contactosPositivos, 4) : 0,
            ProyeccionPromesas  = Math.Round(proyeccionPromesas, 0),
            TotalIncumplidos    = totalIncumplidos,
            TotalAsignados      = totalAsignados,
        };

        // ── Desglose ─────────────────────────────────────────────────────────
        List<FilaLiderDto>  filasLider  = [];
        List<FilaAsesorDto> filasAsesor = [];

        if (!liderID.HasValue)
        {
            // Para el desglose por líder los registros sin AgenteID aún cuentan
            // (titulares sin asesor pero con perfil de gestión); se preserva el null.
            filasLider = await ConstruirFilasLiderAsync(registros
                .Select(r => (r.CarteraID, r.AgenteID, r.UltimoPerfil, r.ValorPromesa))
                .ToList());
        }
        else
        {
            // Para el desglose por asesor los registros con AgenteID NULL no aportan
            // (no hay asesor al cual atribuir la gestión); se filtran fuera.
            filasAsesor = await ConstruirFilasAsesorAsync(registros
                .Where(r => r.AgenteID.HasValue)
                .Select(r => (AgenteID: r.AgenteID!.Value, r.UltimoPerfil, r.ValorPromesa))
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

    /// <summary>Cuándo corrió el ETL por última vez (informativo en el encabezado).</summary>
    private async Task<DateTime?> ObtenerFechaActualizacionAsync(int? liderID, int? carteraID)
    {
        var carteraIDs = await ResolverCarteraIDsAsync(liderID, carteraID);
        var query = AplicarFiltroCarteras(_callCenter.UltimosEstadosTitular, carteraID, carteraIDs);
        return await query.MaxAsync(u => (DateTime?)u.FechaActualizacion);
    }

    /// <summary>Último día con gestiones registradas — determina el "Último ciclo".</summary>
    private async Task<DateOnly?> ObtenerMaxFechaGestionAsync(int? liderID, int? carteraID)
    {
        var carteraIDs = await ResolverCarteraIDsAsync(liderID, carteraID);
        var query = AplicarFiltroCarteras(_callCenter.UltimosEstadosTitular, carteraID, carteraIDs);
        return await query.MaxAsync(u => u.FechaUltimaGestion);
    }

    /// <summary>
    /// Cuenta el universo total de titulares asignados según el filtro activo.
    /// Excluye combinaciones (Líder, Cartera) marcadas como externas.
    /// </summary>
    private async Task<int> ContarAsignadosAsync(int? liderID, int? carteraID)
    {
        var q = _callCenter.TitularCarteras.AsQueryable();
        if (carteraID.HasValue)
        {
            q = q.Where(tc => tc.CarteraID == carteraID.Value);
        }
        else
        {
            // Universo administrativo: excluir asignaciones cuya relación (Líder, Cartera) es externa
            q = q.Where(tc => !_callCenter.LiderCarteras.Any(lc =>
                lc.LiderID == tc.LiderID && lc.CarteraID == tc.CarteraID && lc.EsExterna));
            if (liderID.HasValue)
                q = q.Where(tc => tc.LiderID == liderID.Value);
        }
        return await q.CountAsync();
    }

    /// <summary>
    /// Si se filtra por líder sin cartera específica, devuelve las carteras del líder
    /// cuyas relaciones son internas. Si se filtra por cartera específica, devuelve null.
    /// </summary>
    private async Task<List<int>?> ResolverCarteraIDsAsync(int? liderID, int? carteraID)
    {
        if (carteraID.HasValue || !liderID.HasValue) return null;

        return await _callCenter.LiderCarteras
            .Where(lc => lc.LiderID == liderID.Value && !lc.EsExterna)
            .Select(lc => lc.CarteraID)
            .ToListAsync();
    }

    /// <summary>
    /// Aplica el filtro de cartera a una query sobre UltimoEstadoTitular.
    /// Cuando no hay filtro de cartera ni de líder, restringe a carteras que tienen
    /// al menos una relación interna en Lider_Cartera (excluye carteras exclusivamente
    /// externas del universo administrativo). Nota: UltimoEstadoTitular no expone
    /// LiderID, así que el filtro es a nivel de cartera, no de relación específica —
    /// una cartera "mixta" aporta todos sus titulares al agregado.
    /// </summary>
    private IQueryable<Zentinela.Core.Entities.UltimoEstadoTitular> AplicarFiltroCarteras(
        IQueryable<Zentinela.Core.Entities.UltimoEstadoTitular> q,
        int? carteraID, List<int>? carteraIDs)
    {
        if (carteraID.HasValue)     return q.Where(u => u.CarteraID == carteraID.Value);
        if (carteraIDs is not null) return q.Where(u => carteraIDs.Contains(u.CarteraID));
        return q.Where(u => _callCenter.LiderCarteras.Any(lc =>
            lc.CarteraID == u.CarteraID && !lc.EsExterna));
    }

    private async Task<List<FilaLiderDto>> ConstruirFilasLiderAsync(
        List<(int CarteraID, int? AgenteID, string? UltimoPerfil, decimal? ValorPromesa)> registros)
    {
        // Resolver LiderID por agente real (Cartera_Agente), no por "primer líder
        // de la cartera". Cuando varias relaciones existen para la misma cartera
        // (carteras compartidas), atribuir al líder cuyo agente hizo la gestión
        // — esto alinea el agregado con el drill-down "Ver detalle", que filtra
        // por agentes del líder. Filas con AgenteID NULL o sin asignación en
        // Cartera_Agente quedan fuera del desglose por líder (no hay responsable).
        var pares = registros
            .Where(r => r.AgenteID.HasValue)
            .Select(r => new { r.AgenteID, r.CarteraID })
            .Distinct()
            .ToList();

        if (pares.Count == 0) return [];

        // CarteraAgente.AgenteID es long; UltimoEstadoTitular.AgenteID es int? (FK lógica
        // hacia Agente.AgenteID que en BD es bigint). Convertimos a long para el filtro.
        var agenteIDs   = pares.Select(p => (long)p.AgenteID!.Value).Distinct().ToList();
        var carteraIDs  = pares.Select(p => p.CarteraID).Distinct().ToList();

        var carteraAgentes = await _callCenter.CarteraAgentes
            .Where(ca => agenteIDs.Contains(ca.AgenteID) && carteraIDs.Contains(ca.CarteraID))
            .AsNoTracking()
            .ToListAsync();

        // (AgenteID, CarteraID) → LiderID asignado en Cartera_Agente.
        // Si un agente aparece varias veces para el mismo par (raro), se toma el primero.
        var paresALider = carteraAgentes
            .GroupBy(ca => (AgenteID: ca.AgenteID, ca.CarteraID))
            .ToDictionary(g => g.Key, g => g.First().LiderID);

        // Cargar nombres de los líderes referenciados
        var liderIDs   = paresALider.Values.Distinct().ToList();
        var lideres    = await _callCenter.Lideres
            .Where(l => liderIDs.Contains(l.ID))
            .AsNoTracking()
            .ToDictionaryAsync(l => l.ID, l => l.Nombre);

        var porLider = registros
            .Where(r => r.AgenteID.HasValue
                     && paresALider.ContainsKey(((long)r.AgenteID.Value, r.CarteraID)))
            .GroupBy(r => paresALider[((long)r.AgenteID!.Value, r.CarteraID)])
            .ToList();

        return porLider
            .Select(grp =>
            {
                var total     = grp.Count();
                var contactos = grp.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
                var acuerdos  = grp.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));
                var proyeccion = grp
                    .Where(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty))
                    .Sum(r => r.ValorPromesa ?? 0m);

                return new FilaLiderDto
                {
                    LiderID             = grp.Key,
                    NombreLider         = lideres.TryGetValue(grp.Key, out var nom) ? nom : $"Líder {grp.Key}",
                    TotalGestiones      = total,
                    TasaContactabilidad = total > 0
                        ? Math.Round((decimal)contactos / total, 4) : 0,
                    TasaEfectividad     = contactos > 0
                        ? Math.Round((decimal)acuerdos / contactos, 4) : 0,
                    ProyeccionPromesas  = Math.Round(proyeccion, 0),
                };
            })
            .OrderByDescending(f => f.TotalGestiones)
            .ToList();
    }

    private async Task<List<FilaAsesorDto>> ConstruirFilasAsesorAsync(
        List<(int AgenteID, string? UltimoPerfil, decimal? ValorPromesa)> registros)
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
                var total     = grp.Count();
                var contactos = grp.Count(r => Tipificaciones.ContactoPositivo.Contains(r.UltimoPerfil ?? string.Empty));
                var acuerdos  = grp.Count(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty));
                var proyeccion = grp
                    .Where(r => Tipificaciones.AcuerdoAbono.Contains(r.UltimoPerfil ?? string.Empty))
                    .Sum(r => r.ValorPromesa ?? 0m);

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
                    ProyeccionPromesas  = Math.Round(proyeccion, 0),
                };
            })
            .OrderByDescending(f => f.TotalGestiones)
            .ToList();
    }

    // ── Pagos confirmados (vista director) ────────────────────────────────────

    public async Task<List<PagoDirectorDto>> ObtenerPagosAsync(
        int? liderID, int? carteraID, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        // Universo administrativo: pagos cuya combinación (Líder, Cartera) NO sea externa,
        // salvo cuando el director seleccione una cartera explícita (el dropdown ya filtra).
        var query = _callCenter.Pagos.AsQueryable();

        if (liderID.HasValue)    query = query.Where(p => p.LiderID   == liderID.Value);
        if (carteraID.HasValue)
        {
            query = query.Where(p => p.CarteraID == carteraID.Value);
        }
        else
        {
            query = query.Where(p => !_callCenter.LiderCarteras.Any(lc =>
                lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna));
        }
        if (fechaDesde.HasValue) query = query.Where(p => p.FechaPago >= fechaDesde.Value);
        if (fechaHasta.HasValue) query = query.Where(p => p.FechaPago <= fechaHasta.Value);

        var pagos = await query
            .Join(_callCenter.Carteras,
                  p => p.CarteraID, c => c.ID,
                  (p, c) => new { p, NombreCartera = c.NombreCartera })
            .Join(_callCenter.Lideres,
                  x => x.p.LiderID, l => l.ID,
                  (x, l) => new { x.p, x.NombreCartera, NombreLider = l.Nombre })
            .OrderByDescending(x => x.p.FechaRegistro)
            .AsNoTracking()
            .ToListAsync();

        // Nombres de asesores desde DBRecursosHumanos. Pagos BASE GENERAL
        // (AgenteControlID NULL) muestran la etiqueta literal en lugar de un nombre.
        var agenteIDs = pagos
            .Where(x => x.p.AgenteControlID.HasValue)
            .Select(x => x.p.AgenteControlID!.Value)
            .Distinct()
            .ToList();
        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToListAsync();
        var nombrePorAgente = empleados.ToDictionary(
            e => e.NumeroDocumento,
            e => $"{e.Nombre} {e.Apellidos}".Trim());

        return pagos.Select(x => new PagoDirectorDto
        {
            PagoID        = x.p.PagoID,
            CedulaTitular = x.p.CedulaTitular,
            NombreCliente = x.p.NombreCliente,
            ValorPagado   = x.p.ValorPagado,
            FechaPago     = x.p.FechaPago,
            Honorarios    = x.p.Honorarios,
            NombreCartera = x.NombreCartera,
            NombreLider   = x.NombreLider,
            NombreAsesor  = x.p.AgenteControlID is long agId
                            ? (nombrePorAgente.TryGetValue(agId, out var nombre) ? nombre : $"Agente {agId}")
                            : "BASE GENERAL",
            FechaRegistro = x.p.FechaRegistro,
        }).ToList();
    }
}

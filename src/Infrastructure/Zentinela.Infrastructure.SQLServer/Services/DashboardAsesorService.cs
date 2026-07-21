using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Dashboard;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Construye el dashboard del asesor consultando las 3 bases de datos:
/// - DBCallCenter: UltimoEstadoTitular, HistoricoEstadoTitular, Titular
/// - DBRecursosHumanos: Empleado (para obtener el supervisor)
/// </summary>
public class DashboardAsesorService : IDashboardAsesorService
{
    private readonly CallCenterDbContext _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly ICategoriaRankingService _categoriaRanking;

    public DashboardAsesorService(
        CallCenterDbContext callCenter,
        RecursosHumanosDbContext rrhh,
        ICategoriaRankingService categoriaRanking)
    {
        _callCenter = callCenter;
        _rrhh = rrhh;
        _categoriaRanking = categoriaRanking;
    }

    public async Task<DashboardAsesorDto> ObtenerDashboardAsync(long numeroDocumento, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        // AgenteID en UltimoEstadoTitular / HistoricoEstadoTitular es INT en la BD operacional
        var agenteID = (int)numeroDocumento;

        // Supervisor usa _rrhh → puede correr en paralelo con las consultas a _callCenter.
        // Las consultas a _callCenter deben ser secuenciales (DbContext no es thread-safe).
        var supervisorTask = ObtenerNombreSupervisorAsync(numeroDocumento);

        var acuerdos         = await ObtenerAcuerdosVigentesAsync(agenteID);
        var rendimiento      = await ObtenerRendimientoAsync(agenteID, fechaDesde, fechaHasta);
        var pagosConfirmados = await ObtenerPagosConfirmadosAsync(agenteID, fechaDesde, fechaHasta);
        var categoriaRanking = await ObtenerCategoriaRankingAsync(numeroDocumento, fechaDesde);

        return new DashboardAsesorDto
        {
            NombreSupervisor = await supervisorTask,
            AcuerdosVigentes = acuerdos,
            Rendimiento      = rendimiento,
            PagosConfirmados = pagosConfirmados,
            CategoriaRanking = categoriaRanking,
        };
    }

    // ── Categoría de ranking (Diamante/Oro/Plata/Bronce) ──────────────────────
    // El cálculo vive en CategoriaRankingService (compartido con la futura vista
    // admin/director) y se cachea por año/mes — aquí solo se lee la fila de este agente.
    private async Task<string?> ObtenerCategoriaRankingAsync(long agenteID, DateOnly? fechaDesde)
    {
        var refDate = fechaDesde ?? DateOnly.FromDateTime(DateTime.Today);
        var categorias = await _categoriaRanking.ObtenerCategoriasAsync(refDate.Year, refDate.Month);
        return categorias.TryGetValue(agenteID, out var dto) ? dto.Categoria : null;
    }

    // ── Supervisor ────────────────────────────────────────────────────────────
    private async Task<string?> ObtenerNombreSupervisorAsync(long numeroDocumento)
    {
        var empleado = await _rrhh.Empleados
            .Include(e => e.Supervisor)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.NumeroDocumento == numeroDocumento);

        if (empleado?.Supervisor is null) return null;

        return $"{empleado.Supervisor.Nombre} {empleado.Supervisor.Apellidos}".Trim();
    }

    // ── Acuerdos vigentes ─────────────────────────────────────────────────────
    private async Task<IEnumerable<AcuerdoVigenteDto>> ObtenerAcuerdosVigentesAsync(int agenteID)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var limite = hoy.AddDays(3);

        var acuerdos = await _callCenter.UltimosEstadosTitular
            .Where(u => u.AgenteID == agenteID
                && u.FechaPromesa.HasValue
                && Tipificaciones.AcuerdosVigentes.Contains(u.UltimoPerfil ?? string.Empty))
            .Select(u => new
            {
                u.TitularID,
                NombreTitular = u.Titular.Nombre,
                u.UltimoPerfil,
                u.FechaPromesa,
                u.ValorPromesa,
                u.SaldoTotal,
            })
            .AsNoTracking()
            .ToListAsync();

        return acuerdos.Select(a => new AcuerdoVigenteDto
        {
            TitularID = a.TitularID,
            NombreTitular = a.NombreTitular,
            UltimoPerfil = a.UltimoPerfil!,
            FechaPromesa = a.FechaPromesa!.Value,
            ValorPromesa = a.ValorPromesa,
            SaldoTotal = a.SaldoTotal,
            Semaforo = a.FechaPromesa!.Value <= hoy ? "rojo"
                     : a.FechaPromesa.Value <= limite ? "amarillo"
                     : "verde",
        })
        .OrderBy(a => a.FechaPromesa);
    }

    // ── Rendimiento ───────────────────────────────────────────────────────────
    private async Task<RendimientoAsesorDto> ObtenerRendimientoAsync(int agenteID, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        if (fechaDesde.HasValue && fechaHasta.HasValue)
        {
            // Filtro por período: usar HistoricoEstadoTitular filtrado por FechaUltimaGestion
            var fechaActualizacion = await _callCenter.HistoricoEstadosTitular
                .Where(h => h.AgenteID == agenteID
                         && h.FechaUltimaGestion >= fechaDesde.Value
                         && h.FechaUltimaGestion <= fechaHasta.Value)
                .MaxAsync(h => (DateTime?)h.FechaActualizacion);

            var perfiles = await _callCenter.HistoricoEstadosTitular
                .AsNoTracking()
                .Where(h => h.AgenteID == agenteID
                         && h.FechaUltimaGestion >= fechaDesde.Value
                         && h.FechaUltimaGestion <= fechaHasta.Value)
                .Select(h => h.UltimoPerfil)
                .ToListAsync();

            var totalGestiones    = perfiles.Count;
            var contactosPos      = perfiles.Count(p => Tipificaciones.ContactoPositivo.Contains(p ?? string.Empty));
            var acuerdosAbonos    = perfiles.Count(p => Tipificaciones.AcuerdoAbono.Contains(p ?? string.Empty));

            // Proyección del ciclo: suma de ValorPromesa con FechaPromesa dentro del período
            var proyeccionCiclo = await _callCenter.HistoricoEstadosTitular
                .AsNoTracking()
                .Where(h => h.AgenteID == agenteID
                         && h.FechaPromesa.HasValue
                         && h.ValorPromesa.HasValue
                         && h.FechaPromesa >= fechaDesde.Value
                         && h.FechaPromesa <= fechaHasta.Value
                         && Tipificaciones.AcuerdosVigentes.Contains(h.UltimoPerfil ?? string.Empty))
                .SumAsync(h => (decimal?)h.ValorPromesa) ?? 0m;

            return new RendimientoAsesorDto
            {
                FechaActualizacion  = fechaActualizacion,
                TotalGestiones      = totalGestiones,
                TasaContactabilidad = totalGestiones > 0
                    ? Math.Round((decimal)contactosPos / totalGestiones, 4) : 0,
                TasaEfectividad = contactosPos > 0
                    ? Math.Round((decimal)acuerdosAbonos / contactosPos, 4) : 0,
                ProyeccionCiclo = proyeccionCiclo,
            };
        }

        // Sin filtro: último día con gestión (comportamiento original)
        var ultimaFechaGestion = await _callCenter.UltimosEstadosTitular
            .Where(u => u.AgenteID == agenteID)
            .MaxAsync(u => u.FechaUltimaGestion);

        // FechaActualizacion es solo informativa (cuándo corrió el ETL)
        var fechaActualizacionDefault = await _callCenter.UltimosEstadosTitular
            .Where(u => u.AgenteID == agenteID)
            .MaxAsync(u => (DateTime?)u.FechaActualizacion);

        if (ultimaFechaGestion is null)
            return new RendimientoAsesorDto { FechaActualizacion = fechaActualizacionDefault };

        // Métricas: UltimoEstadoTitular filtrado por agente y último día gestionado
        var perfilesDefault = await _callCenter.UltimosEstadosTitular
            .AsNoTracking()
            .Where(u => u.AgenteID == agenteID && u.FechaUltimaGestion == ultimaFechaGestion)
            .Select(u => u.UltimoPerfil)
            .ToListAsync();

        var totalGestionesD = perfilesDefault.Count;
        var contactosPosD   = perfilesDefault.Count(p => Tipificaciones.ContactoPositivo.Contains(p ?? string.Empty));
        var acuerdosAbD     = perfilesDefault.Count(p => Tipificaciones.AcuerdoAbono.Contains(p ?? string.Empty));

        // Proyección del ciclo actual: acuerdos con FechaPromesa en el mes en curso
        var hoyDefault  = DateOnly.FromDateTime(DateTime.Today);
        var inicioMes   = new DateOnly(hoyDefault.Year, hoyDefault.Month, 1);
        var proyeccionD = await _callCenter.UltimosEstadosTitular
            .AsNoTracking()
            .Where(u => u.AgenteID == agenteID
                     && u.FechaPromesa.HasValue
                     && u.ValorPromesa.HasValue
                     && u.FechaPromesa >= inicioMes
                     && u.FechaPromesa <= hoyDefault
                     && Tipificaciones.AcuerdosVigentes.Contains(u.UltimoPerfil ?? string.Empty))
            .SumAsync(u => (decimal?)u.ValorPromesa) ?? 0m;

        return new RendimientoAsesorDto
        {
            FechaActualizacion  = fechaActualizacionDefault,
            TotalGestiones      = totalGestionesD,
            TasaContactabilidad = totalGestionesD > 0
                ? Math.Round((decimal)contactosPosD / totalGestionesD, 4) : 0,
            TasaEfectividad = contactosPosD > 0
                ? Math.Round((decimal)acuerdosAbD / contactosPosD, 4) : 0,
            ProyeccionCiclo = proyeccionD,
        };
    }

    // ── Detalle de pagos (sub-módulo) ─────────────────────────────────────────
    public async Task<List<PagoAsesorDto>> ObtenerPagosDetalleAsync(
        int agenteID, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        var query = _callCenter.Pagos.Where(p => p.AgenteControlID == agenteID);

        if (fechaDesde.HasValue) query = query.Where(p => p.FechaPago >= fechaDesde.Value);
        if (fechaHasta.HasValue) query = query.Where(p => p.FechaPago <= fechaHasta.Value);

        return await query
            .Join(_callCenter.Carteras,
                  p => p.CarteraID,
                  c => c.ID,
                  (p, c) => new PagoAsesorDto
                  {
                      PagoID        = p.PagoID,
                      CedulaTitular = p.CedulaTitular,
                      NombreCliente = p.NombreCliente,
                      ValorPagado   = p.ValorPagado,
                      FechaPago     = p.FechaPago,
                      Honorarios    = p.Honorarios,
                      NombreCartera = c.NombreCartera,
                      FechaRegistro = p.FechaRegistro,
                  })
            .OrderByDescending(p => p.FechaRegistro)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Pagos confirmados (resumen dashboard) ─────────────────────────────────
    private async Task<PagosConfirmadosDto> ObtenerPagosConfirmadosAsync(int agenteID, DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        // Rango efectivo para filtrar pagos
        var queryPagos = _callCenter.Pagos.Where(p => p.AgenteControlID == agenteID);
        if (fechaDesde.HasValue) queryPagos = queryPagos.Where(p => p.FechaPago >= fechaDesde.Value);
        if (fechaHasta.HasValue) queryPagos = queryPagos.Where(p => p.FechaPago <= fechaHasta.Value);

        // FechaActualizacion: cartera más desactualizada del asesor en el período
        var maxFechasPorCartera = await queryPagos
            .GroupBy(p => p.CarteraID)
            .Select(g => g.Max(p => p.FechaRegistro))
            .ToListAsync();

        var fechaActualizacion = maxFechasPorCartera.Count > 0
            ? maxFechasPorCartera.Min()
            : (DateTime?)null;

        // Totales de recaudo y honorarios en el período
        var totales = await queryPagos
            .GroupBy(p => 1)
            .Select(g => new
            {
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .FirstOrDefaultAsync();

        // Meta de honorarios: usar el mes del período seleccionado
        var refDate = fechaDesde.HasValue
            ? fechaDesde.Value
            : DateOnly.FromDateTime(DateTime.Today);

        var meta = await _callCenter.MetaAgentesMes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AgenteID == agenteID
                                   && m.Año == (short)refDate.Year
                                   && m.Mes == (byte)refDate.Month);

        // Efectividad = acuerdos/abonos sobre total gestiones del período
        var queryRendimiento = _callCenter.HistoricoEstadosTitular
            .AsNoTracking()
            .Where(h => h.AgenteID == agenteID);
        if (fechaDesde.HasValue) queryRendimiento = queryRendimiento.Where(h => h.FechaUltimaGestion >= fechaDesde.Value);
        if (fechaHasta.HasValue) queryRendimiento = queryRendimiento.Where(h => h.FechaUltimaGestion <= fechaHasta.Value);

        var perfiles = await queryRendimiento.Select(h => h.UltimoPerfil).ToListAsync();

        var totalGestiones = perfiles.Count;
        var acuerdosAbonos = perfiles.Count(p => Tipificaciones.AcuerdoAbono.Contains(p ?? string.Empty));

        return new PagosConfirmadosDto
        {
            FechaActualizacion    = fechaActualizacion,
            TotalRecaudo          = totales?.Recaudo    ?? 0,
            TotalHonorarios       = totales?.Honorarios ?? 0,
            MetaHonorarios        = meta?.TipoMeta == 1 ? meta.ValorMeta : 0,
            PorcentajeEfectividad = totalGestiones > 0
                ? Math.Round((decimal)acuerdosAbonos / totalGestiones, 4)
                : 0,
        };
    }
}

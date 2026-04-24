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

    public DashboardAsesorService(
        CallCenterDbContext callCenter,
        RecursosHumanosDbContext rrhh)
    {
        _callCenter = callCenter;
        _rrhh = rrhh;
    }

    public async Task<DashboardAsesorDto> ObtenerDashboardAsync(long numeroDocumento)
    {
        // AgenteID en UltimoEstadoTitular / HistoricoEstadoTitular es INT en la BD operacional
        var agenteID = (int)numeroDocumento;

        // Supervisor usa _rrhh → puede correr en paralelo con las consultas a _callCenter.
        // Las consultas a _callCenter deben ser secuenciales (DbContext no es thread-safe).
        var supervisorTask = ObtenerNombreSupervisorAsync(numeroDocumento);

        var acuerdos         = await ObtenerAcuerdosVigentesAsync(agenteID);
        var rendimiento      = await ObtenerRendimientoAsync(agenteID);
        var pagosConfirmados = await ObtenerPagosConfirmadosAsync(agenteID);

        return new DashboardAsesorDto
        {
            NombreSupervisor = await supervisorTask,
            AcuerdosVigentes = acuerdos,
            Rendimiento      = rendimiento,
            PagosConfirmados = pagosConfirmados,
        };
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
    private async Task<RendimientoAsesorDto> ObtenerRendimientoAsync(int agenteID)
    {
        // Fecha del reporte = último día con gestión registrada para este agente
        var ultimaFechaGestion = await _callCenter.UltimosEstadosTitular
            .Where(u => u.AgenteID == agenteID)
            .MaxAsync(u => u.FechaUltimaGestion);

        // FechaActualizacion es solo informativa (cuándo corrió el ETL)
        var fechaActualizacion = await _callCenter.UltimosEstadosTitular
            .Where(u => u.AgenteID == agenteID)
            .MaxAsync(u => (DateTime?)u.FechaActualizacion);

        if (ultimaFechaGestion is null)
            return new RendimientoAsesorDto { FechaActualizacion = fechaActualizacion };

        // Métricas: UltimoEstadoTitular filtrado por agente y último día gestionado
        var perfiles = await _callCenter.UltimosEstadosTitular
            .AsNoTracking()
            .Where(u => u.AgenteID == agenteID && u.FechaUltimaGestion == ultimaFechaGestion)
            .Select(u => u.UltimoPerfil)
            .ToListAsync();

        var totalGestiones = perfiles.Count;
        var contactosPositivos = perfiles.Count(p => Tipificaciones.ContactoPositivo.Contains(p ?? string.Empty));
        var acuerdosAbonos = perfiles.Count(p => Tipificaciones.AcuerdoAbono.Contains(p ?? string.Empty));

        return new RendimientoAsesorDto
        {
            FechaActualizacion = fechaActualizacion,
            TotalGestiones = totalGestiones,
            TasaContactabilidad = totalGestiones > 0
                ? Math.Round((decimal)contactosPositivos / totalGestiones, 4)
                : 0,
            TasaEfectividad = contactosPositivos > 0
                ? Math.Round((decimal)acuerdosAbonos / contactosPositivos, 4)
                : 0,
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
    private async Task<PagosConfirmadosDto> ObtenerPagosConfirmadosAsync(int agenteID)
    {
        var hoy = DateTime.Today;

        // FechaActualizacion: para cada cartera del asesor, tomar el MAX(FechaRegistro),
        // luego exponer el MIN de esos máximos para evidenciar la cartera más desactualizada.
        var maxFechasPorCartera = await _callCenter.Pagos
            .Where(p => p.AgenteControlID == agenteID)
            .GroupBy(p => p.CarteraID)
            .Select(g => g.Max(p => p.FechaRegistro))
            .ToListAsync();

        var fechaActualizacion = maxFechasPorCartera.Count > 0
            ? maxFechasPorCartera.Min()
            : (DateTime?)null;

        // Totales de recaudo y honorarios (todos los pagos del asesor)
        var totales = await _callCenter.Pagos
            .Where(p => p.AgenteControlID == agenteID)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .FirstOrDefaultAsync();

        // Meta de honorarios del mes actual
        var meta = await _callCenter.MetaAgentesMes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AgenteID == agenteID
                                   && m.Año == (short)hoy.Year
                                   && m.Mes == (byte)hoy.Month);

        // Efectividad = acuerdos/abonos sobre total gestiones
        var perfiles = await _callCenter.UltimosEstadosTitular
            .AsNoTracking()
            .Where(u => u.AgenteID == agenteID)
            .Select(u => u.UltimoPerfil)
            .ToListAsync();

        var totalGestiones = perfiles.Count;
        var acuerdosAbonos = perfiles.Count(p => Tipificaciones.AcuerdoAbono.Contains(p ?? string.Empty));

        return new PagosConfirmadosDto
        {
            FechaActualizacion   = fechaActualizacion,
            TotalRecaudo         = totales?.Recaudo    ?? 0,
            TotalHonorarios      = totales?.Honorarios ?? 0,
            MetaHonorarios       = meta?.TipoMeta == 1 ? meta.ValorMeta : 0,
            PorcentajeEfectividad = totalGestiones > 0
                ? Math.Round((decimal)acuerdosAbonos / totalGestiones, 4)
                : 0,
        };
    }
}

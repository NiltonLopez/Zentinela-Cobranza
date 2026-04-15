using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.InformesRecaudo;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Genera los informes de recaudo consultando DBCallCenter (pagos, metas, agentes, líderes)
/// y DBRecursosHumanos (nombres de los agentes).
///
/// Cálculos temporales clave:
///   - DiasDelMes:       DateTime.DaysInMonth(año, mes)
///   - DiasTranscurridos: día actual si mes=vigente, diasDelMes si mes pasado, 0 si mes futuro
///   - DeberiaLlevarHoy: Meta × (diasTranscurridos / diasDelMes)
///   - ProyeccionCierre: (Recaudo / diasTranscurridos) × diasDelMes
///   - PromedioPagoDia:  Recaudo / días del mes con al menos un pago
/// </summary>
public class InformesRecaudoService : IInformesRecaudoService
{
    private readonly CallCenterDbContext      _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;

    public InformesRecaudoService(
        CallCenterDbContext callCenter,
        RecursosHumanosDbContext rrhh)
    {
        _callCenter = callCenter;
        _rrhh       = rrhh;
    }

    // ── Informe de Agentes ────────────────────────────────────────────────────

    public async Task<InformeAgentesResponse> GetInformeAgentesAsync(int año, int mes)
    {
        var (diasDelMes, diasTranscurridos) = CalcularDias(año, mes);

        // Pagos del mes
        var pagos = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == año && p.FechaPago.Month == mes)
            .AsNoTracking()
            .ToListAsync();

        if (pagos.Count == 0)
            return new InformeAgentesResponse();

        var agenteIDs = pagos.Select(p => p.AgenteControlID).Distinct().ToList();

        // Metas de agentes para el mes
        var metas = await _callCenter.MetaAgentesMes
            .Where(m => m.Año == año && m.Mes == mes && agenteIDs.Contains(m.AgenteID))
            .AsNoTracking()
            .ToDictionaryAsync(m => m.AgenteID, m => m.MetaRecaudo);

        // Líder asociado a cada agente (se toma el primero de sus registros en Cartera_Agente)
        var liderPorAgente = await _callCenter.CarteraAgentes
            .Where(ca => agenteIDs.Contains(ca.AgenteID))
            .GroupBy(ca => ca.AgenteID)
            .Select(g => new { AgenteID = g.Key, LiderID = g.Min(x => x.LiderID) })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AgenteID, x => x.LiderID);

        var liderIDs = liderPorAgente.Values.Distinct().ToList();
        var lideres  = await _callCenter.Lideres
            .Where(l => liderIDs.Contains(l.ID))
            .AsNoTracking()
            .ToDictionaryAsync(l => l.ID, l => l.Nombre);

        // Nombres desde RRHH: AgenteID coincide con NumeroDocumento del empleado
        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        // Agrupar y calcular por agente
        var agentes = pagos
            .GroupBy(p => p.AgenteControlID)
            .Select(g =>
            {
                var agenteID   = g.Key;
                var recaudo    = g.Sum(p => p.ValorPagado);
                var honorarios = g.Sum(p => p.Honorarios);
                var gestionado = g.Select(p => p.CedulaTitular).Distinct().Count();
                var diasConPago = g.Select(p => p.FechaPago).Distinct().Count();
                var meta        = metas.TryGetValue(agenteID, out var m) ? m : 0m;
                var tieneMeta   = metas.ContainsKey(agenteID);
                var liderID     = liderPorAgente.TryGetValue(agenteID, out var lid) ? lid : 0;

                var deberiaHoy = diasDelMes > 0
                    ? meta * diasTranscurridos / diasDelMes
                    : 0m;

                return new InformeAgenteMesDto
                {
                    AgenteID         = agenteID,
                    NombreAgente     = empleados.TryGetValue(agenteID, out var nombre) ? nombre : $"Agente {agenteID}",
                    NombreLider      = lideres.TryGetValue(liderID, out var lNombre) ? lNombre : "Sin líder",
                    Gestionado       = gestionado,
                    Recaudo          = recaudo,
                    Honorarios       = honorarios,
                    Meta             = meta,
                    TieneMeta        = tieneMeta,
                    DeberiaLlevarHoy = deberiaHoy,
                    FaltaParaMeta    = Math.Max(0m, meta - recaudo),
                    PromedioPagoDia  = diasConPago > 0 ? recaudo / diasConPago : 0m,
                };
            })
            .OrderBy(a => a.Recaudo)
            .ToList();

        return new InformeAgentesResponse
        {
            TotalRecaudo    = agentes.Sum(a => a.Recaudo),
            TotalHonorarios = agentes.Sum(a => a.Honorarios),
            TotalAgentes    = agentes.Count,
            TotalGestionado = agentes.Sum(a => a.Gestionado),
            Agentes         = agentes,
        };
    }

    // ── Informe de Líderes ────────────────────────────────────────────────────

    public async Task<InformeLideresResponse> GetInformeLideresAsync(int año, int mes)
    {
        var (diasDelMes, diasTranscurridos) = CalcularDias(año, mes);

        // Recaudo del mes por líder-cartera
        var pagos = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == año && p.FechaPago.Month == mes)
            .GroupBy(p => new { p.LiderID, p.CarteraID })
            .Select(g => new
            {
                g.Key.LiderID,
                g.Key.CarteraID,
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .AsNoTracking()
            .ToListAsync();

        // Metas del mes
        var metas = await _callCenter.MetaLiderCarteraMes
            .Where(m => m.Año == año && m.Mes == mes)
            .AsNoTracking()
            .ToListAsync();

        // Todas las relaciones líder-cartera activas (con navegación a nombres)
        var liderCarteras = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .AsNoTracking()
            .ToListAsync();

        // Capacidad: titulares asignados por líder-cartera
        var capacidades = await _callCenter.TitularCarteras
            .GroupBy(tc => new { tc.LiderID, tc.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Capacidad = g.Count() })
            .AsNoTracking()
            .ToListAsync();

        var filas = liderCarteras.Select(lc =>
        {
            var pago = pagos.FirstOrDefault(p => p.LiderID == lc.LiderID && p.CarteraID == lc.CarteraID);
            var meta = metas.FirstOrDefault(m => m.LiderID == lc.LiderID && m.CarteraID == lc.CarteraID);
            var cap  = capacidades.FirstOrDefault(c => c.LiderID == lc.LiderID && c.CarteraID == lc.CarteraID);

            var recaudo    = pago?.Recaudo    ?? 0m;
            var honorarios = pago?.Honorarios ?? 0m;
            var metaV      = meta?.MetaRecaudo ?? 0m;
            var capacidad  = cap?.Capacidad ?? 0;

            var deberiaHoy = diasDelMes > 0
                ? metaV * diasTranscurridos / diasDelMes
                : 0m;

            var proyeccion = diasTranscurridos > 0
                ? recaudo / diasTranscurridos * diasDelMes
                : 0m;

            return new InformeLiderCarteraDto
            {
                LiderID          = lc.LiderID,
                NombreLider      = lc.Lider.Nombre,
                CarteraID        = lc.CarteraID,
                NombreCartera    = lc.Cartera.NombreCartera,
                MetaCartera      = metaV,
                TieneMeta        = meta != null,
                Capacidad        = capacidad,
                Recaudo          = recaudo,
                Honorarios       = honorarios,
                DeberiaLlevarHoy = deberiaHoy,
                ProyeccionCierre = proyeccion,
            };
        })
        .OrderBy(f => f.NombreLider)
        .ThenBy(f => f.NombreCartera)
        .ToList();

        return new InformeLideresResponse
        {
            TotalRecaudo = filas.Sum(f => f.Recaudo),
            TotalMeta    = filas.Sum(f => f.MetaCartera),
            TotalLideres = filas.Select(f => f.LiderID).Distinct().Count(),
            LiderCarteras = filas,
        };
    }

    // ── Informe de Carteras (comparativo) ─────────────────────────────────────

    public async Task<InformeCarterasResponse> GetInformeCarterasAsync(
        int añoA, int mesA, int añoB, int mesB)
    {
        // Recaudo por cartera para período A
        var recaudoA = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == añoA && p.FechaPago.Month == mesA)
            .GroupBy(p => p.CarteraID)
            .Select(g => new
            {
                CarteraID  = g.Key,
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.CarteraID);

        // Recaudo por cartera para período B
        var recaudoB = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == añoB && p.FechaPago.Month == mesB)
            .GroupBy(p => p.CarteraID)
            .Select(g => new
            {
                CarteraID  = g.Key,
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.CarteraID);

        // Todas las carteras que aparecen en alguno de los dos períodos
        var carteraIDs = recaudoA.Keys.Union(recaudoB.Keys).ToList();
        var carteras   = await _callCenter.Carteras
            .Where(c => carteraIDs.Contains(c.ID))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.ID, c => c.NombreCartera);

        var filas = carteraIDs
            .Select(cid =>
            {
                var rA = recaudoA.TryGetValue(cid, out var a) ? a.Recaudo    : 0m;
                var hA = recaudoA.TryGetValue(cid, out var a2) ? a2.Honorarios : 0m;
                var rB = recaudoB.TryGetValue(cid, out var b) ? b.Recaudo    : 0m;
                var hB = recaudoB.TryGetValue(cid, out var b2) ? b2.Honorarios : 0m;

                return new InformeCarteraComparativoDto
                {
                    CarteraID           = cid,
                    NombreCartera       = carteras.TryGetValue(cid, out var nombre) ? nombre : $"Cartera {cid}",
                    RecaudoA            = rA,
                    HonorariosA         = hA,
                    RecaudoB            = rB,
                    HonorariosB         = hB,
                    VariacionRecaudo    = rB - rA,
                    PorcentajeVariacion = rA > 0 ? Math.Round((rB - rA) / rA * 100, 1) : null,
                };
            })
            .OrderBy(f => f.NombreCartera)
            .ToList();

        return new InformeCarterasResponse
        {
            PeriodoA = new PeriodoDto { Año = añoA, Mes = mesA, Nombre = NombreMes(añoA, mesA) },
            PeriodoB = new PeriodoDto { Año = añoB, Mes = mesB, Nombre = NombreMes(añoB, mesB) },
            TotalRecaudoA  = filas.Sum(f => f.RecaudoA),
            TotalRecaudoB  = filas.Sum(f => f.RecaudoB),
            VariacionTotal = filas.Sum(f => f.VariacionRecaudo),
            Carteras       = filas,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calcula (diasDelMes, diasTranscurridos) para el período dado.
    /// Si el mes es el vigente → diasTranscurridos = día actual.
    /// Si el mes ya pasó       → diasTranscurridos = diasDelMes (mes completo).
    /// Si el mes no ha llegado → diasTranscurridos = 0.
    /// </summary>
    private static (int DiasDelMes, int DiasTranscurridos) CalcularDias(int año, int mes)
    {
        var hoy       = DateOnly.FromDateTime(DateTime.Today);
        int diasDelMes = DateTime.DaysInMonth(año, mes);

        int diasTranscurridos;
        if (año == hoy.Year && mes == hoy.Month)
            diasTranscurridos = hoy.Day;
        else if (new DateOnly(año, mes, 1) < new DateOnly(hoy.Year, hoy.Month, 1))
            diasTranscurridos = diasDelMes;
        else
            diasTranscurridos = 0;

        return (diasDelMes, diasTranscurridos);
    }

    private static readonly string[] _meses =
    [
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    ];

    private static string NombreMes(int año, int mes) => $"{_meses[mes - 1]} {año}";
}

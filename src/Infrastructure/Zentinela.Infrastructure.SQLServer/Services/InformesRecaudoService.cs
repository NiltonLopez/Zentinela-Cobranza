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
///   - DiasDelMes:                DateTime.DaysInMonth(año, mes)
///   - DiasTranscurridos:         día actual si mes=vigente, diasDelMes si mes pasado, 0 si mes futuro
///   - DiasHabilesDelMes:         días hábiles del mes (sin sábados, domingos ni festivos Colombia)
///   - DiasHabilesTranscurridos:  días hábiles transcurridos hasta hoy (o fin del mes si pasado)
///   - DeberiaLlevarHoy:          Meta × (diasHabilesTranscurridos / diasHabilesDelMes)
///   - ProyeccionCierre:          (Recaudo / diasTranscurridos) × diasDelMes
///   - PromedioPagoDia:           Recaudo / días del mes con al menos un pago
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
        var (diasDelMes, diasTranscurridos)           = CalcularDias(año, mes);
        var (diasHabilesDelMes, diasHabilesTransc)    = CalcularDiasHabiles(año, mes);

        // Universo administrativo: pagos cuya combinación (Líder, Cartera) NO está
        // marcada como externa en Lider_Cartera. Pagos sin relación viva en
        // Lider_Cartera (histórico) se consideran internos por defecto.
        var pagos = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == año && p.FechaPago.Month == mes
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna))
            .AsNoTracking()
            .ToListAsync();

        if (pagos.Count == 0)
            return new InformeAgentesResponse();

        // Pagos BASE GENERAL (AgenteControlID NULL) no aplican al informe por agente —
        // su recaudo se imputa solo al líder y se ve en GetInformeLideresAsync.
        var pagosConAgente = pagos.Where(p => p.AgenteControlID.HasValue).ToList();
        var agenteIDs = pagosConAgente.Select(p => p.AgenteControlID!.Value).Distinct().ToList();

        // Metas de agentes para el mes
        var metasRaw = await _callCenter.MetaAgentesMes
            .Where(m => m.Año == año && m.Mes == mes && agenteIDs.Contains(m.AgenteID))
            .AsNoTracking()
            .ToListAsync();
        var metasValores = metasRaw.ToDictionary(m => m.AgenteID, m => m.ValorMeta);
        var metasTipo    = metasRaw.ToDictionary(m => m.AgenteID, m => m.TipoMeta == 1 ? "Honorarios" : "Recaudo");

        // Líder y cartera asociados a cada agente (se toma el mínimo de Cartera_Agente)
        var asignaciones = await _callCenter.CarteraAgentes
            .Where(ca => agenteIDs.Contains(ca.AgenteID))
            .GroupBy(ca => ca.AgenteID)
            .Select(g => new
            {
                AgenteID  = g.Key,
                LiderID   = g.Min(x => x.LiderID),
                CarteraID = g.Min(x => x.CarteraID),
            })
            .AsNoTracking()
            .ToListAsync();

        var liderPorAgente   = asignaciones.ToDictionary(x => x.AgenteID, x => x.LiderID);
        var carteraPorAgente = asignaciones.ToDictionary(x => x.AgenteID, x => x.CarteraID);

        var liderIDs = liderPorAgente.Values.Distinct().ToList();
        var lideres  = await _callCenter.Lideres
            .Where(l => liderIDs.Contains(l.ID))
            .AsNoTracking()
            .ToDictionaryAsync(l => l.ID, l => l.Nombre);

        var carteraIDs = carteraPorAgente.Values.Distinct().ToList();
        var carteras   = await _callCenter.Carteras
            .Where(c => carteraIDs.Contains(c.ID))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.ID, c => c.NombreCartera);

        // Nombres desde RRHH: AgenteID coincide con NumeroDocumento del empleado
        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        // Agrupar y calcular por agente (excluye BASE GENERAL ya filtrado arriba)
        var agentes = pagosConAgente
            .GroupBy(p => p.AgenteControlID!.Value)
            .Select(g =>
            {
                var agenteID   = g.Key;
                var recaudo    = g.Sum(p => p.ValorPagado);
                var honorarios = g.Sum(p => p.Honorarios);
                var gestionado = g.Select(p => p.CedulaTitular).Distinct().Count();
                var diasConPago = g.Select(p => p.FechaPago).Distinct().Count();
                var tieneMeta  = metasValores.ContainsKey(agenteID);
                var valorMeta  = metasValores.TryGetValue(agenteID, out var mv) ? mv : 0m;
                var tipoMeta   = metasTipo.TryGetValue(agenteID, out var mt) ? mt : null;
                var liderID    = liderPorAgente.TryGetValue(agenteID, out var lid) ? lid : 0;

                var deberiaHoy = diasHabilesDelMes > 0
                    ? valorMeta * diasHabilesTransc / diasHabilesDelMes
                    : 0m;

                var metricaVsMeta = tipoMeta == "Honorarios" ? honorarios : recaudo;
                var carteraID     = carteraPorAgente.TryGetValue(agenteID, out var cid) ? cid : 0;

                return new InformeAgenteMesDto
                {
                    AgenteID         = agenteID,
                    NombreAgente     = empleados.TryGetValue(agenteID, out var nombre) ? nombre : $"Agente {agenteID}",
                    LiderID          = liderID,
                    NombreLider      = lideres.TryGetValue(liderID, out var lNombre) ? lNombre : "Sin líder",
                    NombreCartera    = carteras.TryGetValue(carteraID, out var cNombre) ? cNombre : string.Empty,
                    Gestionado       = gestionado,
                    Recaudo          = recaudo,
                    Honorarios       = honorarios,
                    ValorMeta        = valorMeta,
                    TipoMeta         = tipoMeta,
                    TieneMeta        = tieneMeta,
                    DeberiaLlevarHoy = deberiaHoy,
                    FaltaParaMeta    = Math.Max(0m, valorMeta - metricaVsMeta),
                    PromedioPagoDia  = diasConPago > 0 ? recaudo / diasConPago : 0m,
                };
            })
            .OrderBy(a => a.Recaudo)
            .ToList();

        // Total de agentes activos en la empresa: asignados en CarteraAgentes con estado activo en RRHH
        var todosAgenteIDs = await _callCenter.CarteraAgentes
            .Select(ca => ca.AgenteID)
            .Distinct()
            .ToListAsync();

        var totalActivos = await _rrhh.Empleados
            .Where(e => todosAgenteIDs.Contains(e.NumeroDocumento) && e.EstadoEmpleado)
            .CountAsync();

        return new InformeAgentesResponse
        {
            TotalRecaudo        = agentes.Sum(a => a.Recaudo),
            TotalHonorarios     = agentes.Sum(a => a.Honorarios),
            TotalAgentes        = agentes.Count,
            TotalAgentesActivos = totalActivos,
            TotalGestionado     = agentes.Sum(a => a.Gestionado),
            Agentes             = agentes,
        };
    }

    // ── Informe de Líderes ────────────────────────────────────────────────────

    public async Task<InformeLideresResponse> GetInformeLideresAsync(int año, int mes)
    {
        var (diasDelMes, diasTranscurridos)           = CalcularDias(año, mes);
        var (diasHabilesDelMes, diasHabilesTransc)    = CalcularDiasHabiles(año, mes);

        // Recaudo del mes por líder-cartera (excluye relaciones externas)
        var pagos = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == año && p.FechaPago.Month == mes
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna))
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

        // Metas del mes (solo relaciones internas)
        var metas = await _callCenter.MetaLiderCarteraMes
            .Where(m => m.Año == año && m.Mes == mes
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == m.LiderID && lc.CarteraID == m.CarteraID && lc.EsExterna))
            .AsNoTracking()
            .ToListAsync();

        // Todas las relaciones líder-cartera internas (con navegación a nombres)
        var liderCarteras = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .Where(lc => !lc.EsExterna)
            .AsNoTracking()
            .ToListAsync();

        // Capacidad: titulares asignados por líder-cartera
        var capacidades = await _callCenter.TitularCarteras
            .GroupBy(tc => new { tc.LiderID, tc.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Capacidad = g.Count() })
            .AsNoTracking()
            .ToListAsync();

        // Asesores activos por combinación líder-cartera
        var asesoresPorLiderCartera = await _callCenter.CarteraAgentes
            .GroupBy(ca => new { ca.LiderID, ca.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Total = g.Select(x => x.AgenteID).Distinct().Count() })
            .AsNoTracking()
            .ToListAsync();

        var filas = liderCarteras.Select(liderCartera =>
        {
            var pago = pagos.FirstOrDefault(p => p.LiderID == liderCartera.LiderID && p.CarteraID == liderCartera.CarteraID);
            var meta = metas.FirstOrDefault(m => m.LiderID == liderCartera.LiderID && m.CarteraID == liderCartera.CarteraID);
            var cap  = capacidades.FirstOrDefault(c => c.LiderID == liderCartera.LiderID && c.CarteraID == liderCartera.CarteraID);

            var recaudo    = pago?.Recaudo    ?? 0m;
            var honorarios = pago?.Honorarios ?? 0m;
            var valorMeta  = meta?.ValorMeta  ?? 0m;
            var tipoMeta   = meta == null ? null : (meta.TipoMeta == 1 ? "Honorarios" : "Recaudo");
            var capacidad  = cap?.Capacidad   ?? 0;

            var deberiaHoy = diasHabilesDelMes > 0
                ? valorMeta * diasHabilesTransc / diasHabilesDelMes
                : 0m;

            var proyeccion = diasTranscurridos > 0
                ? recaudo / diasTranscurridos * diasDelMes
                : 0m;

            return new InformeLiderCarteraDto
            {
                LiderID          = liderCartera.LiderID,
                NombreLider      = liderCartera.Lider.Nombre,
                CarteraID        = liderCartera.CarteraID,
                NombreCartera    = liderCartera.Cartera.NombreCartera,
                TotalAsesores    = asesoresPorLiderCartera.FirstOrDefault(x => x.LiderID == liderCartera.LiderID && x.CarteraID == liderCartera.CarteraID)?.Total ?? 0,
                ValorMeta        = valorMeta,
                TipoMeta         = tipoMeta,
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
            TotalMeta    = filas.Sum(f => f.ValorMeta),
            TotalLideres = filas.Select(f => f.LiderID).Distinct().Count(),
            LiderCarteras = filas,
        };
    }

    // ── Informe de Carteras (comparativo) ─────────────────────────────────────

    public async Task<InformeCarterasResponse> GetInformeCarterasAsync(
        int añoA, int mesA, int añoB, int mesB)
    {
        // Recaudo por cartera para período A (excluye pagos con relación externa)
        var recaudoA = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == añoA && p.FechaPago.Month == mesA
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna))
            .GroupBy(p => p.CarteraID)
            .Select(g => new
            {
                CarteraID  = g.Key,
                Recaudo    = g.Sum(p => p.ValorPagado),
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.CarteraID);

        // Recaudo por cartera para período B (excluye pagos con relación externa)
        var recaudoB = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == añoB && p.FechaPago.Month == mesB
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna))
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

    // ── Informe de Agencias Externas ──────────────────────────────────────────

    public async Task<InformeAgenciasExternasResponse> GetInformeAgenciasExternasAsync(int año, int mes)
    {
        // Universo: solo relaciones externas (Lider_Cartera.EsExterna = 1).
        // Una misma cartera puede aparecer dos veces si tiene una relación externa
        // y otra interna con líderes distintos.
        var liderCarteras = await _callCenter.LiderCarteras
            .Include(lc => lc.Lider)
            .Include(lc => lc.Cartera)
            .Where(lc => lc.EsExterna && lc.Lider.Activo && lc.Cartera.Activa)
            .AsNoTracking()
            .ToListAsync();

        if (liderCarteras.Count == 0)
            return new InformeAgenciasExternasResponse();

        var paresExternos = liderCarteras
            .Select(lc => new { lc.LiderID, lc.CarteraID })
            .ToHashSet();

        // Recaudo del mes por (líder, cartera) limitado a las combinaciones externas.
        // Filtramos en BD por las carteraIDs y luego cribamos en memoria por par exacto.
        var carteraIDs = liderCarteras.Select(lc => lc.CarteraID).Distinct().ToList();
        var liderIDs   = liderCarteras.Select(lc => lc.LiderID).Distinct().ToList();

        var pagosRaw = await _callCenter.Pagos
            .Where(p => p.FechaPago.Year == año && p.FechaPago.Month == mes
                     && carteraIDs.Contains(p.CarteraID)
                     && liderIDs.Contains(p.LiderID))
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

        var pagos = pagosRaw
            .Where(p => paresExternos.Contains(new { p.LiderID, p.CarteraID }))
            .ToList();

        // Total de asignaciones (capacity) por (líder, cartera) — mismo cribado por par.
        var asignacionesRaw = await _callCenter.TitularCarteras
            .Where(tc => carteraIDs.Contains(tc.CarteraID) && liderIDs.Contains(tc.LiderID))
            .GroupBy(tc => new { tc.LiderID, tc.CarteraID })
            .Select(g => new { g.Key.LiderID, g.Key.CarteraID, Total = g.Count() })
            .AsNoTracking()
            .ToListAsync();

        var asignaciones = asignacionesRaw
            .Where(a => paresExternos.Contains(new { a.LiderID, a.CarteraID }))
            .ToList();

        var filas = liderCarteras
            .Select(lc =>
            {
                var pago = pagos.FirstOrDefault(p => p.LiderID == lc.LiderID && p.CarteraID == lc.CarteraID);
                var asig = asignaciones.FirstOrDefault(a => a.LiderID == lc.LiderID && a.CarteraID == lc.CarteraID);

                return new FilaAgenciaExternaDto
                {
                    LiderID           = lc.LiderID,
                    NombreLider       = lc.Lider.Nombre,
                    CarteraID         = lc.CarteraID,
                    NombreCartera     = lc.Cartera.NombreCartera,
                    Recaudo           = pago?.Recaudo    ?? 0m,
                    Honorarios        = pago?.Honorarios ?? 0m,
                    TotalAsignaciones = asig?.Total      ?? 0,
                };
            })
            .OrderBy(f => f.NombreLider)
            .ThenBy(f => f.NombreCartera)
            .ToList();

        return new InformeAgenciasExternasResponse
        {
            TotalRecaudo      = filas.Sum(f => f.Recaudo),
            TotalHonorarios   = filas.Sum(f => f.Honorarios),
            TotalAsignaciones = filas.Sum(f => f.TotalAsignaciones),
            TotalAgencias     = filas.Select(f => f.LiderID).Distinct().Count(),
            Filas             = filas,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (int DiasDelMes, int DiasTranscurridos) CalcularDias(int año, int mes)
    {
        var hoy        = DateOnly.FromDateTime(DateTime.Today);
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

    // Festivos oficiales de Colombia 2024-2027.
    // Fijos: Año Nuevo, Día del Trabajo, Independencia, Boyacá, Inmaculada, Navidad.
    // Ley Emiliani (traslado al lunes siguiente): Reyes Magos, San José, San Pedro y San Pablo,
    //   Asunción, Día de la Raza, Todos los Santos, Independencia de Cartagena.
    // Semana Santa + móviles de Pascua: Jueves Santo, Viernes Santo, Ascensión, Corpus Christi,
    //   Sagrado Corazón.
    private static readonly HashSet<DateOnly> _festivosColombia =
    [
        // ── 2024 (Pascua: 31-mar) ────────────────────────────────────────────
        new(2024,  1,  1), new(2024,  1,  8), new(2024,  3, 25), new(2024,  3, 28),
        new(2024,  3, 29), new(2024,  5,  1), new(2024,  5, 13), new(2024,  6,  3),
        new(2024,  6, 10), new(2024,  7,  1), new(2024,  7, 20), new(2024,  8,  7),
        new(2024,  8, 19), new(2024, 10, 14), new(2024, 11,  4), new(2024, 11, 11),
        new(2024, 12,  8), new(2024, 12, 25),

        // ── 2025 (Pascua: 20-abr) ────────────────────────────────────────────
        new(2025,  1,  1), new(2025,  1,  6), new(2025,  3, 24), new(2025,  4, 17),
        new(2025,  4, 18), new(2025,  5,  1), new(2025,  6,  2), new(2025,  6, 23),
        new(2025,  6, 30), new(2025,  7, 20), new(2025,  8,  7), new(2025,  8, 18),
        new(2025, 10, 13), new(2025, 11,  3), new(2025, 11, 17), new(2025, 12,  8),
        new(2025, 12, 25),

        // ── 2026 (Pascua: 05-abr) ────────────────────────────────────────────
        new(2026,  1,  1), new(2026,  1, 12), new(2026,  3, 23), new(2026,  4,  2),
        new(2026,  4,  3), new(2026,  5,  1), new(2026,  5, 18), new(2026,  6,  8),
        new(2026,  6, 15), new(2026,  6, 29), new(2026,  7, 20), new(2026,  8,  7),
        new(2026,  8, 17), new(2026, 10, 12), new(2026, 11,  2), new(2026, 11, 16),
        new(2026, 12,  8), new(2026, 12, 25),

        // ── 2027 (Pascua: 28-mar) ────────────────────────────────────────────
        new(2027,  1,  1), new(2027,  1, 11), new(2027,  3, 22), new(2027,  3, 25),
        new(2027,  3, 26), new(2027,  5,  1), new(2027,  5, 10), new(2027,  5, 31),
        new(2027,  6,  7), new(2027,  7,  5), new(2027,  7, 20), new(2027,  8,  7),
        new(2027,  8, 16), new(2027, 10, 18), new(2027, 11,  1), new(2027, 11, 15),
        new(2027, 12,  8), new(2027, 12, 25),
    ];

    private static bool EsDiaHabil(DateOnly d) =>
        d.DayOfWeek is not DayOfWeek.Sunday &&
        !_festivosColombia.Contains(d);

    private static (int DiasHabilesDelMes, int DiasHabilesTranscurridos) CalcularDiasHabiles(int año, int mes)
    {
        var hoy       = DateOnly.FromDateTime(DateTime.Today);
        int totalDias = DateTime.DaysInMonth(año, mes);

        DateOnly limite;
        if (año == hoy.Year && mes == hoy.Month)
            limite = hoy;
        else if (new DateOnly(año, mes, 1) < new DateOnly(hoy.Year, hoy.Month, 1))
            limite = new DateOnly(año, mes, totalDias);
        else
            limite = new DateOnly(año, mes, 1).AddDays(-1); // mes futuro → 0

        int habilesDelMes        = 0;
        int habilesTranscurridos = 0;

        for (var d = new DateOnly(año, mes, 1); d.Month == mes; d = d.AddDays(1))
        {
            if (!EsDiaHabil(d)) continue;
            habilesDelMes++;
            if (d <= limite) habilesTranscurridos++;
        }

        return (habilesDelMes, habilesTranscurridos);
    }

    private static readonly string[] _meses =
    [
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    ];

    private static string NombreMes(int año, int mes) => $"{_meses[mes - 1]} {año}";
}

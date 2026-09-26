using Zentinela.Application.Settings;

namespace Zentinela.Application.Utils;

// Etapa 4 de los Informes de Localización (ADR-040): tendencia semanal y ranking de asesores. Cálculos puros,
// sin I/O (TendenciaAsesoresTests).

/// <summary>El corte que representa a una semana en la tendencia.</summary>
public sealed record CorteSemanal(DateTime InicioSemana, int CorteID, DateTime FechaCorte);

/// <summary>
/// Tendencia semanal: por cada semana (lunes a domingo) se toma el ÚLTIMO corte OK de esa semana. Un día con
/// tres cortes (13:00, dos cargues) no pesa más que un día con uno, y la semana en curso muestra el corte más
/// reciente. Las semanas sin cortes no aparecen (el gráfico no inventa puntos).
/// </summary>
public static class TendenciaLocalizacion
{
    /// <summary>Lunes de la semana de <paramref name="fecha"/>.</summary>
    public static DateTime InicioSemana(DateTime fecha)
    {
        var d = fecha.Date;
        var desdeLunes = ((int)d.DayOfWeek + 6) % 7;   // lunes = 0 … domingo = 6
        return d.AddDays(-desdeLunes);
    }

    /// <summary>Lunes de la semana más vieja que entra en las últimas <paramref name="semanas"/> (incluida la actual).</summary>
    public static DateTime Desde(DateTime hoy, int semanas) =>
        InicioSemana(hoy).AddDays(-7 * (Math.Max(1, semanas) - 1));

    /// <summary>
    /// Un corte por semana, de la más vieja a la más reciente: el de <c>FechaCorte</c> mayor (a igual fecha, el de
    /// CorteID mayor). Descarta los anteriores a <see cref="Desde"/> y los posteriores a hoy (un reloj adelantado).
    /// </summary>
    public static IReadOnlyList<CorteSemanal> CortesSemanales(
        IEnumerable<(int CorteID, DateTime FechaCorte)> cortesOk, DateTime hoy, int semanas)
    {
        var desde = Desde(hoy, semanas);
        var hasta = hoy.Date.AddDays(1);
        return cortesOk
            .Where(c => c.FechaCorte >= desde && c.FechaCorte < hasta)
            .GroupBy(c => InicioSemana(c.FechaCorte))
            .Select(g =>
            {
                var u = g.OrderByDescending(c => c.FechaCorte).ThenByDescending(c => c.CorteID).First();
                return new CorteSemanal(g.Key, u.CorteID, u.FechaCorte);
            })
            .OrderBy(c => c.InicioSemana)
            .ToList();
    }

    /// <summary>
    /// El último corte OK de la semana ANTERIOR a la de <paramref name="hoy"/> (alertas: "cayó contra la semana
    /// pasada"); null si esa semana no tuvo cortes.
    /// </summary>
    public static CorteSemanal? SemanaAnterior(IEnumerable<(int CorteID, DateTime FechaCorte)> cortesOk, DateTime hoy)
    {
        var anterior = InicioSemana(hoy).AddDays(-7);
        return CortesSemanales(cortesOk, hoy, 2).FirstOrDefault(c => c.InicioSemana == anterior);
    }
}

// ── Asesores ──────────────────────────────────────────────────────────────────

/// <summary>
/// Métricas de un asesor en un universo (un par, una cartera, un líder o todo). La unidad es la fila de
/// <c>UltimoEstadoTitular</c> (titular + cartera) de los pares activos cuya ÚLTIMA gestión hizo el asesor
/// (<c>UltimoEstadoTitular.AgenteID</c>, verificado contra HistoricoEstadoTitular el 2026-09-26: es quien hizo
/// la última gestión, no un asignado fijo). Es lo que guarda <c>LocalizacionResumenAsesor</c> por par.
/// </summary>
public sealed record MetricasAsesor(
    int Titulares,
    int ConDato,
    int ConSaldo,
    int SinContactoConSaldo,
    decimal SaldoSinContacto,
    int GestionadosRecientes,
    int ContactoEfectivo)
{
    public static readonly MetricasAsesor Vacias = new(0, 0, 0, 0, 0, 0, 0);

    public static MetricasAsesor Sumar(IEnumerable<MetricasAsesor> partes) =>
        partes.Aggregate(Vacias, (a, b) => new(
            a.Titulares + b.Titulares, a.ConDato + b.ConDato, a.ConSaldo + b.ConSaldo,
            a.SinContactoConSaldo + b.SinContactoConSaldo, a.SaldoSinContacto + b.SaldoSinContacto,
            a.GestionadosRecientes + b.GestionadosRecientes, a.ContactoEfectivo + b.ContactoEfectivo));
}

/// <summary>
/// Criterios del ranking de asesores. <b>Contactabilidad</b> (predeterminado): % de sus titulares cuya última
/// gestión fue con contacto. <b>Gestion</b>: % gestionados en los últimos N días. <b>ConDato</b>: % con celular o
/// email en alguna fuente de localización. <b>SinContactoSaldo</b>: % con saldo y sin dato de contacto (menos es
/// mejor; null si el universo no tiene saldo en UltimoEstadoTitular, como en el ranking de líderes).
/// </summary>
public enum CriterioRankingAsesor { Contactabilidad, Gestion, ConDato, SinContactoSaldo }

public static class RankingAsesoresCalculo
{
    public static bool TryParseCriterio(string? texto, out CriterioRankingAsesor criterio)
    {
        criterio = CriterioRankingAsesor.Contactabilidad;
        if (string.IsNullOrWhiteSpace(texto)) return true;
        return Enum.TryParse(texto.Trim(), ignoreCase: true, out criterio) && Enum.IsDefined(criterio);
    }

    public static bool MenorEsMejor(CriterioRankingAsesor c) => c == CriterioRankingAsesor.SinContactoSaldo;

    public static decimal? Valor(MetricasAsesor m, CriterioRankingAsesor c) => c switch
    {
        CriterioRankingAsesor.Contactabilidad  => CalculoLocalizacion.Pct(m.ContactoEfectivo, m.Titulares),
        CriterioRankingAsesor.Gestion          => CalculoLocalizacion.Pct(m.GestionadosRecientes, m.Titulares),
        CriterioRankingAsesor.ConDato          => CalculoLocalizacion.Pct(m.ConDato, m.Titulares),
        CriterioRankingAsesor.SinContactoSaldo => PctSinContactoConSaldo(m),
        _ => null,
    };

    public static decimal? PctSinContactoConSaldo(MetricasAsesor m) =>
        m.ConSaldo <= 0 ? null : CalculoLocalizacion.Pct(m.SinContactoConSaldo, m.Titulares);

    /// <summary>
    /// Rankea a los asesores con al menos <paramref name="minTitulares"/> titulares; el resto va aparte, sin
    /// posición (de más a menos titulares). Empates y orden como <see cref="CalculoLocalizacion.Rankear"/>.
    /// </summary>
    public static (IReadOnlyList<FilaRankeada<T>> Ranking, IReadOnlyList<T> PocosTitulares) Rankear<T>(
        IEnumerable<T> asesores, Func<T, MetricasAsesor> metricas, Func<T, string> nombre,
        CriterioRankingAsesor criterio, int minTitulares)
    {
        var lista = asesores.ToList();
        var min = Math.Max(1, minTitulares);
        var entran = lista.Where(a => metricas(a).Titulares >= min).ToList();
        var pocos = lista.Where(a => metricas(a).Titulares < min && metricas(a).Titulares > 0)
                         .OrderByDescending(a => metricas(a).Titulares).ThenBy(nombre, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        var ranking = CalculoLocalizacion.Rankear(entran, a => Valor(metricas(a), criterio), nombre, MenorEsMejor(criterio));
        return (ranking, pocos);
    }
}

public static class ParametrosAsesores
{
    /// <summary>Días de "gestionado recientemente", saneados (el precálculo los recibe como parámetro).</summary>
    public static int DiasGestion(AsesoresLocalizacionSettings s) => Math.Clamp(s.DiasGestionReciente, 1, 365);
}

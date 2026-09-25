using Zentinela.Application.Settings;

namespace Zentinela.Application.Utils;

/// <summary>Criterio de los rankings de localización (ADR-040). En la etapa 3 se suman Uso y Retorno.</summary>
public enum CriterioRankingLocalizacion { Indice, Contacto, Cobertura, Frescura, SinContactoSaldo }

/// <summary>
/// Métricas que cruzan fuentes para un universo de titulares (un par, una cartera, un líder o toda
/// la base). Es lo que guardan <c>LocalizacionResumenPar</c> y <c>LocalizacionResumenLider</c>.
/// </summary>
public sealed record MetricasLocalizacion(
    int Total,
    int ConContacto,
    int ConContactoFuncional,
    int BuscadosAlguna,
    int BuscadosFuncional,
    int Frescos180,
    int ConSaldo,
    int SinContactoConSaldo,
    decimal SaldoSinContacto,
    decimal SaldoTotal)
{
    public static readonly MetricasLocalizacion Vacias = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Suma de universos disjuntos (p. ej. los pares de una cartera).</summary>
    public static MetricasLocalizacion Sumar(IEnumerable<MetricasLocalizacion> partes) =>
        partes.Aggregate(Vacias, (a, b) => new(
            a.Total + b.Total, a.ConContacto + b.ConContacto, a.ConContactoFuncional + b.ConContactoFuncional,
            a.BuscadosAlguna + b.BuscadosAlguna, a.BuscadosFuncional + b.BuscadosFuncional,
            a.Frescos180 + b.Frescos180, a.ConSaldo + b.ConSaldo, a.SinContactoConSaldo + b.SinContactoConSaldo,
            a.SaldoSinContacto + b.SaldoSinContacto, a.SaldoTotal + b.SaldoTotal));
}

/// <summary>Una fila ya posicionada de un ranking.</summary>
public sealed record FilaRankeada<T>(int Posicion, T Item, decimal? Valor);

/// <summary>Lo que ve un Líder del ranking de líderes: el top más su propia fila (ADR-040).</summary>
public sealed record RecorteRanking<T>(IReadOnlyList<FilaRankeada<T>> Filas, int? MiPosicion, int Total, bool Recortado);

/// <summary>
/// Cálculos puros de los Informes de Localización (ADR-040): porcentajes, índice y rankings.
/// Sin I/O, para poder probarlos (RankingCalculoTests).
/// </summary>
public static class CalculoLocalizacion
{
    /// <summary>Porcentaje 0–100 con dos decimales; null si no hay universo.</summary>
    public static decimal? Pct(int parte, int total) =>
        total <= 0 ? null : Math.Round(parte * 100m / total, 2, MidpointRounding.AwayFromZero);

    public static decimal? PctContacto(MetricasLocalizacion m) => Pct(m.ConContacto, m.Total);
    public static decimal? PctCobertura(MetricasLocalizacion m) => Pct(m.BuscadosFuncional, m.Total);
    public static decimal? PctFrescura(MetricasLocalizacion m) => Pct(m.Frescos180, m.Total);
    /// <summary>
    /// Sin contacto y con saldo sobre el total. Null si el universo no tiene NINGÚN titular con saldo: hay
    /// carteras cuyo saldo no llega a UltimoEstadoTitular (hoy 16 activas: las Delmar, LATIN, Mil
    /// Decoraciones, Interenlace…; SaldoTotal NULL o sin filas) y un 0 % ahí
    /// no es "bien localizado" sino "sin dato"; con null quedan al final del ranking y sin alerta.
    /// </summary>
    public static decimal? PctSinContactoConSaldo(MetricasLocalizacion m) =>
        m.ConSaldo <= 0 ? null : Pct(m.SinContactoConSaldo, m.Total);

    /// <summary>
    /// Índice de localización 0–100: promedio ponderado de %contacto, %cobertura funcional y
    /// %frescura, dividido por la suma de los pesos. Null si no hay titulares o los pesos no suman.
    /// </summary>
    public static decimal? Indice(MetricasLocalizacion m, InformesLocalizacionSettings p)
    {
        var suma = p.PesoContacto + p.PesoCobertura + p.PesoFrescura;
        if (m.Total <= 0 || suma <= 0) return null;
        var valor = (p.PesoContacto  * m.ConContacto
                   + p.PesoCobertura * m.BuscadosFuncional
                   + p.PesoFrescura  * m.Frescos180) * 100m / (m.Total * suma);
        return Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>En "sin contacto con saldo" gana el que menos tiene; en el resto, el que más.</summary>
    public static bool MenorEsMejor(CriterioRankingLocalizacion c) => c == CriterioRankingLocalizacion.SinContactoSaldo;

    /// <summary>Valor que ordena el ranking, en la misma escala que se muestra (0–100).</summary>
    public static decimal? ValorCriterio(MetricasLocalizacion m, CriterioRankingLocalizacion c, InformesLocalizacionSettings p) =>
        c switch
        {
            CriterioRankingLocalizacion.Indice           => Indice(m, p),
            CriterioRankingLocalizacion.Contacto         => PctContacto(m),
            CriterioRankingLocalizacion.Cobertura        => PctCobertura(m),
            CriterioRankingLocalizacion.Frescura         => PctFrescura(m),
            CriterioRankingLocalizacion.SinContactoSaldo => PctSinContactoConSaldo(m),
            _ => null,
        };

    public static bool TryParseCriterio(string? texto, out CriterioRankingLocalizacion criterio)
    {
        criterio = CriterioRankingLocalizacion.Indice;
        if (string.IsNullOrWhiteSpace(texto)) return true;
        return Enum.TryParse(texto.Trim(), ignoreCase: true, out criterio)
               && Enum.IsDefined(criterio);
    }

    /// <summary>
    /// Ordena y numera. Empates: dos valores iguales a un decimal (lo que se ve en pantalla) comparten
    /// posición y el siguiente salta ("1, 2, 2, 4"); dentro del empate, por nombre. Los que no tienen
    /// valor (universo vacío) van al final sin posición propia: comparten la última.
    /// </summary>
    public static IReadOnlyList<FilaRankeada<T>> Rankear<T>(
        IEnumerable<T> items, Func<T, decimal?> valor, Func<T, string> nombre, bool menorEsMejor)
    {
        var conValor = items
            .Select(i => (Item: i, Valor: Redondear(valor(i)), Nombre: nombre(i)))
            .ToList();

        var ordenados = conValor
            .OrderBy(x => x.Valor is null ? 1 : 0)
            .ThenBy(x => menorEsMejor ? x.Valor : -x.Valor)
            .ThenBy(x => x.Nombre, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-CO"), true))
            .ToList();

        var resultado = new List<FilaRankeada<T>>(ordenados.Count);
        for (var i = 0; i < ordenados.Count; i++)
        {
            var pos = i > 0 && ordenados[i].Valor == ordenados[i - 1].Valor
                ? resultado[i - 1].Posicion
                : i + 1;
            resultado.Add(new FilaRankeada<T>(pos, ordenados[i].Item, ordenados[i].Valor));
        }
        return resultado;
    }

    /// <summary>
    /// Lo que recibe un Líder: las filas con posición ≤ <paramref name="top"/> (con los empates de la
    /// última posición del top, que no se cortan a mitad) más su propia fila si quedó fuera. Nunca la
    /// lista completa. <c>MiPosicion</c> es null si el líder no aparece en el ranking.
    /// </summary>
    public static RecorteRanking<T> RecortarParaLider<T>(
        IReadOnlyList<FilaRankeada<T>> ranking, Func<T, bool> esPropio, int top)
    {
        var propia = ranking.FirstOrDefault(f => esPropio(f.Item));
        var filas = ranking.Where(f => f.Posicion <= top).ToList();
        if (propia is not null && !filas.Contains(propia)) filas.Add(propia);
        return new RecorteRanking<T>(filas, propia?.Posicion, ranking.Count, filas.Count < ranking.Count);
    }

    private static decimal? Redondear(decimal? v) =>
        v is null ? null : Math.Round(v.Value, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Cuándo hace falta un corte nuevo (ADR-040). Misma regla de corte de las 13:00 que la caché del
/// Calificador (<c>CalificadorCarteraCache</c>): la carga nocturna de las fuentes ya terminó a esa hora.
/// </summary>
public static class ReglaCorteLocalizacion
{
    public static readonly TimeSpan HoraCorte = TimeSpan.FromHours(13);

    /// <summary>El corte de las 13:00 más reciente que ya pasó: hoy si ya son las 13:00, si no ayer.</summary>
    public static DateTime UltimoCorteProgramado(DateTime ahora)
    {
        var corte = ahora.Date.Add(HoraCorte);
        return ahora < corte ? corte.AddDays(-1) : corte;
    }

    /// <summary>
    /// ¿Hay que recalcular? Sí si no hay corte OK, o si el último es anterior al corte de las 13:00 más
    /// reciente (primer acceso después de las 13:00). Al arrancar la API, además, si no hay corte del día.
    /// </summary>
    public static bool DebeRecalcular(DateTime? ultimoCorteOk, DateTime ahora, bool alArrancar)
    {
        if (ultimoCorteOk is null) return true;
        if (ultimoCorteOk.Value < UltimoCorteProgramado(ahora)) return true;
        return alArrancar && ultimoCorteOk.Value.Date < ahora.Date;
    }
}

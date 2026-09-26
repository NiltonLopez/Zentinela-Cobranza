using System.Globalization;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Settings;

namespace Zentinela.Application.Utils;

// "Alertas de la semana" de los Informes de Localización (ADR-040, etapa 4). Solo se muestran dentro de la
// app (panel del Resumen): no se envían por ningún canal. Reglas puras, sin I/O (AlertasSemanaTests); el
// servicio arma las entradas desde el corte, los lotes y el catálogo de fuentes.

/// <summary>Índice de un líder en el corte actual y en el último corte de la semana anterior.</summary>
public sealed record IndiceSemanalLider(int LiderID, string Nombre, long? Documento, decimal? Actual, decimal? Anterior);

/// <summary>Un lote ENVIADO (estado actual) con su vínculo de retorno.</summary>
public sealed record LoteEnviadoAlerta(
    int EnvioID, string Codigo, int LiderID, string NombreLider, long? DocumentoLider,
    int CarteraID, string NombreCartera, string Fuente, DateTime FechaEnvio, long? CargueEjecucionID, int Titulares);

/// <summary>Una fuente del catálogo con su último cargue OK del Cargue de Fuentes.</summary>
/// <param name="UltimoDato">Consulta más reciente de la fuente en toda la base (último corte).</param>
public sealed record FuenteCargueAlerta(string Clave, string Etiqueta, EstadoFuente Estado, DateTime? UltimoCargueOk, DateTime? UltimoDato);

/// <summary>Una cartera (o la parte de un líder en ella) con sus métricas del corte.</summary>
public sealed record CarteraAlerta(int CarteraID, string Nombre, int? LiderID, string? NombreLider, MetricasLocalizacion Metricas);

/// <summary>Cola sugerida aproximada de un líder (del corte) y sus lotes enviados en la ventana.</summary>
/// <param name="Cola">Búsquedas sugeridas (titular × fuente Funcional), la aproximación del corte de la etapa 3.</param>
/// <param name="NuncaBuscados">Titulares que no se han buscado en ninguna fuente Funcional (contexto del mensaje).</param>
public sealed record ColaLiderAlerta(int LiderID, string Nombre, long? Documento, int Cola, int NuncaBuscados, int LotesRecientes);

public static class AlertasSemanaLocalizacion
{
    public const string CaidaIndice = "CAIDA_INDICE";
    public const string LoteSinRetorno = "LOTE_SIN_RETORNO";
    public const string FuenteSinCargue = "FUENTE_SIN_CARGUE";
    public const string CarteraSinContacto = "CARTERA_SIN_CONTACTO";
    public const string ColaSinLotes = "COLA_SIN_LOTES";

    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] Orden = [CaidaIndice, CarteraSinContacto, LoteSinRetorno, ColaSinLotes, FuenteSinCargue];

    /// <summary>Líder cuyo índice cayó MÁS de <c>CaidaIndicePuntos</c> contra el último corte de la semana anterior.</summary>
    public static IEnumerable<AlertaSemanaDto> CaidasDeIndice(IEnumerable<IndiceSemanalLider> lideres, AlertasSemanaLocalizacionSettings u)
    {
        foreach (var l in lideres)
        {
            if (l.Actual is not { } a || l.Anterior is not { } b) continue;
            // Mismo redondeo que se ve (un decimal): una caída de 3,04 que se muestra "3,0" no pasa un umbral de 3.
            var caida = Math.Round(b, 1, MidpointRounding.AwayFromZero) - Math.Round(a, 1, MidpointRounding.AwayFromZero);
            if (caida <= u.CaidaIndicePuntos) continue;
            yield return new AlertaSemanaDto
            {
                Tipo = CaidaIndice, Severidad = "alta", LiderID = l.LiderID, NombreLider = l.Nombre, DocumentoLider = l.Documento,
                Magnitud = caida,
                Mensaje = $"{l.Nombre}: el índice de localización bajó {caida.ToString("0.0", Co)} puntos "
                        + $"({b.ToString("0.0", Co)} → {a.ToString("0.0", Co)}) contra la semana anterior.",
            };
        }
    }

    /// <summary>
    /// Lote ENVIADO hace más de <c>DiasLoteSinRetorno</c> días sin cargue de retorno vinculado. El vínculo lo pone
    /// el sistema cuando un cargue OK de la fuente trae titulares del lote (etapa 4), así que "sin vínculo" = no
    /// ha vuelto nada de ese lote por el Cargue de Fuentes.
    /// </summary>
    public static IEnumerable<AlertaSemanaDto> LotesSinRetorno(IEnumerable<LoteEnviadoAlerta> lotes, DateTime hoy, AlertasSemanaLocalizacionSettings u)
    {
        foreach (var l in lotes)
        {
            if (l.CargueEjecucionID is not null) continue;
            var dias = (hoy.Date - l.FechaEnvio.Date).Days;
            if (dias <= u.DiasLoteSinRetorno) continue;
            yield return new AlertaSemanaDto
            {
                Tipo = LoteSinRetorno, Severidad = "media", LiderID = l.LiderID, NombreLider = l.NombreLider,
                DocumentoLider = l.DocumentoLider, CarteraID = l.CarteraID, EnvioID = l.EnvioID, Fuente = l.Fuente, Magnitud = dias,
                Mensaje = $"{l.Codigo} ({l.Fuente}, {l.NombreCartera} — {l.NombreLider}, {l.Titulares.ToString("N0", Co)} titulares): "
                        + $"enviado hace {dias.ToString("N0", Co)} días y todavía sin retorno.",
            };
        }
    }

    /// <summary>
    /// Fuente Funcional sin datos nuevos en más de <c>DiasFuenteSinCargue</c> días. La evidencia de un cargue es la más
    /// reciente entre el último cargue OK del Cargue de Fuentes y el dato más reciente de la fuente en la base (último
    /// corte): las fuentes que todavía se cargan con los SP legacy no dejan fila en <c>CargueFuenteEjecucion</c>, pero sí
    /// fechas nuevas (el legacy no refrescaba la fecha del contenido idéntico: si una carga solo trajo lo mismo, aquí
    /// parece más vieja de lo que es; ADR-039). Sin ninguna de las dos: "sin datos".
    /// </summary>
    public static IEnumerable<AlertaSemanaDto> FuentesSinCargue(IEnumerable<FuenteCargueAlerta> fuentes, DateTime hoy, AlertasSemanaLocalizacionSettings u)
    {
        foreach (var f in fuentes.Where(f => f.Estado == EstadoFuente.Funcional))
        {
            var ultimo = Max(f.UltimoCargueOk?.Date, f.UltimoDato?.Date);
            if (ultimo is null)
            {
                yield return new AlertaSemanaDto
                {
                    Tipo = FuenteSinCargue, Severidad = "media", Fuente = f.Clave, Magnitud = int.MaxValue,
                    Mensaje = $"{f.Etiqueta} (Funcional): no tiene cargues registrados ni datos en la base.",
                };
                continue;
            }
            var dias = (hoy.Date - ultimo.Value).Days;
            if (dias <= u.DiasFuenteSinCargue) continue;
            var origen = f.UltimoCargueOk is { } c && c.Date >= ultimo
                ? $"último cargue el {c.ToString("dd/MM/yyyy", Co)}"
                : $"dato más reciente del {ultimo.Value.ToString("dd/MM/yyyy", Co)}"
                  + (f.UltimoCargueOk is null ? ", sin cargues registrados en el Cargue de Fuentes" : "");
            yield return new AlertaSemanaDto
            {
                Tipo = FuenteSinCargue, Severidad = "media", Fuente = f.Clave, Magnitud = dias,
                Mensaje = $"{f.Etiqueta} (Funcional): {dias.ToString("N0", Co)} días sin datos nuevos ({origen}).",
            };
        }
    }

    private static DateTime? Max(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a > b ? a : b);

    /// <summary>Cartera con MÁS de <c>SinContactoConSaldoPct</c> % de sus titulares sin contacto y con saldo.</summary>
    public static IEnumerable<AlertaSemanaDto> CarterasSinContacto(IEnumerable<CarteraAlerta> carteras, AlertasSemanaLocalizacionSettings u)
    {
        foreach (var c in carteras)
        {
            if (CalculoLocalizacion.PctSinContactoConSaldo(c.Metricas) is not { } pct || pct <= u.SinContactoConSaldoPct) continue;
            var quien = c.NombreLider is null ? c.Nombre : $"{c.Nombre} — {c.NombreLider}";
            yield return new AlertaSemanaDto
            {
                Tipo = CarteraSinContacto, Severidad = "alta", CarteraID = c.CarteraID, LiderID = c.LiderID, NombreLider = c.NombreLider,
                Magnitud = pct,
                Mensaje = $"{quien}: {pct.ToString("0.#", Co)} % de sus titulares tiene saldo y no tiene dato de contacto "
                        + $"({c.Metricas.SinContactoConSaldo.ToString("N0", Co)}, {c.Metricas.SaldoSinContacto.ToString("C0", Co)}).",
            };
        }
    }

    /// <summary>Líder con cola sugerida de MÁS de <c>ColaGrande</c> titulares × fuente y ningún lote enviado en la ventana.</summary>
    public static IEnumerable<AlertaSemanaDto> ColasSinLotes(IEnumerable<ColaLiderAlerta> colas, AlertasSemanaLocalizacionSettings u)
    {
        foreach (var c in colas)
        {
            if (c.Cola <= u.ColaGrande || c.LotesRecientes > 0) continue;
            yield return new AlertaSemanaDto
            {
                Tipo = ColaSinLotes, Severidad = "media", LiderID = c.LiderID, NombreLider = c.Nombre, DocumentoLider = c.Documento,
                Magnitud = c.Cola,
                Mensaje = $"{c.Nombre}: {c.Cola.ToString("N0", Co)} búsquedas sugeridas (titular × fuente Funcional, aproximado del corte; "
                        + $"{c.NuncaBuscados.ToString("N0", Co)} titulares nunca buscados en una Funcional) y ningún lote "
                        + $"enviado a localización en los últimos {u.DiasSinLotes} días.",
            };
        }
    }

    /// <summary>Alta primero; dentro de la severidad, por tipo y de mayor a menor magnitud.</summary>
    public static List<AlertaSemanaDto> Ordenar(IEnumerable<AlertaSemanaDto> alertas) =>
        alertas
            .OrderBy(a => a.Severidad == "alta" ? 0 : 1)
            .ThenBy(a => Array.IndexOf(Orden, a.Tipo) is var i && i >= 0 ? i : 99)
            .ThenByDescending(a => a.Magnitud ?? 0)
            .ThenBy(a => a.Mensaje, StringComparer.Ordinal)
            .ToList();
}

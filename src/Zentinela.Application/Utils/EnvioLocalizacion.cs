using Zentinela.Application.DTOs.InformesLocalizacion;

namespace Zentinela.Application.Utils;

// Envíos a localización (ADR-040, etapa 3): estados de los lotes, retorno, impacto y uso del departamento.
// Todo lo que se decide en C# vive aquí, sin I/O, para probarlo (EnviosLocalizacionTests); el SQL solo
// agrega por lote (GeneradorSqlEnvios).

/// <summary>Estado de un lote de envío a localización (CHECK de <c>LocalizacionEnvio.Estado</c>).</summary>
public enum EstadoEnvio { Borrador, Enviado, Cerrado, Anulado }

/// <summary>Lo que se le puede hacer a un lote después de crearlo.</summary>
public enum AccionEnvio { Enviar, Cerrar, Anular }

/// <summary>
/// Máquina de estados de los lotes (ADR-040, etapa 3): BORRADOR → ENVIADO → CERRADO, y ANULADO desde
/// BORRADOR o ENVIADO. Un CERRADO o un ANULADO ya no cambian. El SQL de la transición actualiza solo si
/// el estado en la BD sigue siendo uno de <see cref="Origenes"/> (concurrencia optimista): si otro usuario
/// lo movió antes, la actualización no afecta filas y la API responde 409.
/// </summary>
public static class TransicionesEnvio
{
    public static string Codigo(EstadoEnvio e) => e switch
    {
        EstadoEnvio.Borrador => "BORRADOR",
        EstadoEnvio.Enviado  => "ENVIADO",
        EstadoEnvio.Cerrado  => "CERRADO",
        _                    => "ANULADO",
    };

    public static bool TryParse(string? codigo, out EstadoEnvio estado)
    {
        foreach (var e in Enum.GetValues<EstadoEnvio>())
            if (string.Equals(Codigo(e), codigo?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                estado = e;
                return true;
            }
        estado = EstadoEnvio.Borrador;
        return false;
    }

    public static bool TryParseAccion(string? texto, out AccionEnvio accion) =>
        Enum.TryParse(texto?.Trim(), ignoreCase: true, out accion) && Enum.IsDefined(accion);

    /// <summary>Estados desde los que se puede aplicar la acción.</summary>
    public static IReadOnlyList<EstadoEnvio> Origenes(AccionEnvio a) => a switch
    {
        AccionEnvio.Enviar => [EstadoEnvio.Borrador],
        AccionEnvio.Cerrar => [EstadoEnvio.Enviado],
        _                  => [EstadoEnvio.Borrador, EstadoEnvio.Enviado],
    };

    public static EstadoEnvio Destino(AccionEnvio a) => a switch
    {
        AccionEnvio.Enviar => EstadoEnvio.Enviado,
        AccionEnvio.Cerrar => EstadoEnvio.Cerrado,
        _                  => EstadoEnvio.Anulado,
    };

    public static bool Puede(EstadoEnvio actual, AccionEnvio a) => Origenes(a).Contains(actual);

    /// <summary>Mensaje para el 409 cuando el lote no está en un estado que admita la acción.</summary>
    public static string MensajeNoPermitido(string codigo, EstadoEnvio actual, AccionEnvio a)
    {
        var verbo = a switch { AccionEnvio.Enviar => "enviar", AccionEnvio.Cerrar => "cerrar", _ => "anular" };
        var desde = string.Join(" o ", Origenes(a).Select(Codigo));
        return $"No se puede {verbo} el lote {codigo}: está {Codigo(actual)} (solo se puede desde {desde}). "
             + "Puede que otro usuario lo haya cambiado; recarga la lista.";
    }
}

/// <summary>Un cargue del Cargue de Fuentes (<c>CargueFuenteEjecucion</c>) candidato a ser el retorno de un lote.</summary>
public sealed record CargueRetorno(long EjecucionID, string Fuente, string? Usuario, string Estado, DateTime? FechaFin, DateTime? Sello);

/// <summary>
/// Desde cuándo un dato de la fuente cuenta como "buscado tras el envío". Con un cargue de retorno: su
/// <c>SelloFechaConsulta</c>, inclusive (el cargue escribe ese sello en las filas que inserta o refresca).
/// Sin cargue registrado: la fecha del envío, estricta.
/// </summary>
public sealed record UmbralRetorno(DateTime Desde, bool Inclusivo, CargueRetorno? Cargue);

/// <summary>
/// Retorno de un lote (ADR-040, etapa 3). La "vuelta" de un lote es el PRIMER cargue OK de su fuente que
/// terminó después del envío (sin las corridas del harness de pruebas). No se sabe qué archivo devolvió
/// qué lote (etapa 4: vínculo explícito), así que es una aproximación: si dos lotes de la misma fuente
/// se enviaron antes del mismo cargue, los dos lo toman como retorno.
/// </summary>
public static class RetornoEnvio
{
    /// <summary>
    /// El cargue que cuenta como retorno. <paramref name="vinculado"/> (el <c>CargueEjecucionID</c> ya guardado
    /// en el lote, etapa 4) manda si existe y está OK; si no, el primer cargue OK de la fuente con
    /// <c>FechaFin</c> posterior al envío. <paramref name="esDePrueba"/> descarta las corridas del harness.
    /// </summary>
    public static CargueRetorno? ElegirCargue(
        string fuente, DateTime? fechaEnvio, IEnumerable<CargueRetorno> cargues,
        Func<string?, bool> esDePrueba, long? vinculado = null)
    {
        if (fechaEnvio is null) return null;
        var validos = cargues
            .Where(c => c.Fuente.Equals(fuente, StringComparison.OrdinalIgnoreCase)
                     && c.Estado.Equals("OK", StringComparison.OrdinalIgnoreCase)
                     && c.FechaFin is not null
                     && !esDePrueba(c.Usuario))
            .ToList();

        if (vinculado is { } id && validos.FirstOrDefault(c => c.EjecucionID == id) is { } v) return v;

        return validos
            .Where(c => c.FechaFin > fechaEnvio)
            .OrderBy(c => c.FechaFin)
            .ThenBy(c => c.EjecucionID)
            .FirstOrDefault();
    }

    /// <summary>Umbral de "buscado tras el envío"; null si el lote no se ha enviado.</summary>
    public static UmbralRetorno? Umbral(DateTime? fechaEnvio, CargueRetorno? cargue)
    {
        if (fechaEnvio is null) return null;
        if (cargue?.Sello is { } sello) return new UmbralRetorno(sello, Inclusivo: true, cargue);
        // Un cargue OK siempre tiene sello; si faltara, se cae a la fecha del envío.
        return new UmbralRetorno(fechaEnvio.Value, Inclusivo: false, cargue);
    }

    /// <summary>La misma regla que aplica el SQL a la última fila del titular en la fuente.</summary>
    public static bool EsBuscado(DateTime? fechaDato, UmbralRetorno? u) =>
        u is not null && fechaDato is { } f && (f > u.Desde || (u.Inclusivo && f == u.Desde));
}

/// <summary>
/// Lo que el SQL agrega por lote (una fila por lote, <c>GeneradorSqlEnvios.Metricas</c>). Titulares =
/// filas del detalle; el resto, titulares que cumplen cada condición.
/// </summary>
public sealed record MetricasCrudasEnvio(
    int Titulares,
    int Buscados,
    int Localizados,
    DateTime? PrimerDato,
    int Gestionados,
    int PasaronAContacto,
    int TitularesConPago,
    int Pagos,
    decimal ValorPagado,
    decimal Honorarios,
    int PromesasVigentes)
{
    public static readonly MetricasCrudasEnvio Vacias = new(0, 0, 0, null, 0, 0, 0, 0, 0, 0, 0);

    public static MetricasCrudasEnvio Sumar(IEnumerable<MetricasCrudasEnvio> partes) =>
        partes.Aggregate(Vacias, (a, b) => new(
            a.Titulares + b.Titulares, a.Buscados + b.Buscados, a.Localizados + b.Localizados,
            a.PrimerDato is null ? b.PrimerDato : b.PrimerDato is null ? a.PrimerDato : (a.PrimerDato < b.PrimerDato ? a.PrimerDato : b.PrimerDato),
            a.Gestionados + b.Gestionados, a.PasaronAContacto + b.PasaronAContacto,
            a.TitularesConPago + b.TitularesConPago, a.Pagos + b.Pagos,
            a.ValorPagado + b.ValorPagado, a.Honorarios + b.Honorarios, a.PromesasVigentes + b.PromesasVigentes));
}

/// <summary>Retorno e impacto de un lote a partir de lo que agregó el SQL. Sin I/O: se prueba en memoria.</summary>
public static class MetricasEnvio
{
    /// <summary>
    /// Retorno del lote; null si no se ha enviado. La fecha de retorno es el fin del cargue que lo devolvió o,
    /// sin cargue registrado, el primer dato nuevo de la fuente para algún titular del lote.
    /// </summary>
    public static RetornoEnvioDto? Retorno(DateTime? fechaEnvio, UmbralRetorno? u, MetricasCrudasEnvio m)
    {
        if (fechaEnvio is null || u is null) return null;
        var fechaRetorno = u.Cargue?.FechaFin ?? (m.Buscados > 0 ? m.PrimerDato : null);
        return new RetornoEnvioDto
        {
            Base                   = u.Cargue is null ? "FECHA" : "CARGUE",
            CargueEjecucionID      = u.Cargue?.EjecucionID,
            FechaCargue            = u.Cargue?.FechaFin,
            BuscadosDesde          = u.Desde,
            Buscados               = m.Buscados,
            PctRetorno             = CalculoLocalizacion.Pct(m.Buscados, m.Titulares),
            Localizados            = m.Localizados,
            PctLocalizadosBuscados = CalculoLocalizacion.Pct(m.Localizados, m.Buscados),
            PctLocalizados         = CalculoLocalizacion.Pct(m.Localizados, m.Titulares),
            FechaRetorno           = fechaRetorno,
            DiasRetorno            = fechaRetorno is { } f ? Math.Max(0, (f.Date - fechaEnvio.Value.Date).Days) : null,
        };
    }

    /// <summary>Impacto en la gestión; null si el lote no se ha enviado.</summary>
    public static ImpactoEnvioDto? Impacto(DateTime? fechaEnvio, MetricasCrudasEnvio m) =>
        fechaEnvio is null ? null : new ImpactoEnvioDto
        {
            Gestionados      = m.Gestionados,
            PctGestionados   = CalculoLocalizacion.Pct(m.Gestionados, m.Titulares),
            PasaronAContacto = m.PasaronAContacto,
            TitularesConPago = m.TitularesConPago,
            Pagos            = m.Pagos,
            ValorPagado      = m.ValorPagado,
            Honorarios       = m.Honorarios,
            PromesasVigentes = m.PromesasVigentes,
        };
}

/// <summary>
/// "Perfil con contacto" para el impacto de un lote (ADR-040): el UltimoPerfil dice que se habló con el
/// titular o con un tercero. Son las categorías con contacto de <see cref="Config.PerfilClasificacion"/>
/// (ADR-029): ContactoDirecto, Acuerdos (Promesa) y Tercero (el perfil menciona "tercero" o "familiar").
/// Ya pagó no cuenta: los pagos se miden aparte y un "pago reflejado" no implica contacto.
/// Comparación normalizada (<see cref="PerfilesSugerencia.Normalizar"/>): sin tildes, mayúsculas, espacios colapsados.
/// </summary>
public static class PerfilesContacto
{
    private static readonly HashSet<string> Claves =
        Config.PerfilClasificacion.ContactoDirecto
            .Concat(Config.PerfilClasificacion.Acuerdos)
            .Select(PerfilesSugerencia.Normalizar)
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Palabras que hacen de un perfil un contacto con tercero (misma regla que ADR-029).</summary>
    public static readonly IReadOnlyList<string> PalabrasTercero = ["TERCERO", "FAMILIAR"];

    /// <summary>Claves normalizadas de los conjuntos cerrados (el SQL las recibe como JSON).</summary>
    public static IReadOnlyCollection<string> ClavesNormalizadas => Claves;

    public static bool EsConContacto(string? perfil)
    {
        var p = PerfilesSugerencia.Normalizar(perfil);
        if (p.Length == 0) return false;
        return Claves.Contains(p) || PalabrasTercero.Any(x => p.Contains(x, StringComparison.Ordinal));
    }
}

/// <summary>Un lote enviado con sus métricas, para agregarlo por líder, cartera o par.</summary>
public sealed record UsoLote(int EnvioID, int LiderID, int CarteraID, DateTime FechaEnvio, MetricasCrudasEnvio Metricas, int? DiasRetorno);

/// <summary>
/// Cola sugerida aproximada de un par (ADR-040): suma, sobre sus fuentes Funcionales del último corte, de
/// "Falta por buscar" más min(No encontrados, buscados con dato de más de 180 días). Es un techo de los
/// candidatos (titular × fuente) de las Sugerencias ANTES de exclusiones: el corte no separa los no
/// encontrados viejos de los encontrados viejos, ni conoce perfiles, promesas o saldo. Recalcular las
/// sugerencias de cada par costaría 0,5–4 s por par (minutos para el departamento completo).
/// </summary>
public sealed record ColaPar(int LiderID, int CarteraID, int Cola);

/// <summary>Uso del departamento de localización de un grupo de lotes (un líder, una cartera, un par o todos).</summary>
public sealed record UsoAgregado(
    int Lotes,
    MetricasCrudasEnvio Metricas,
    int? Cola,
    decimal? PctColaEnviada,
    decimal? PctRetorno,
    decimal? PctLocalizados,
    int LotesConRetorno,
    decimal? DiasRetornoPromedio);

public static class UsoDepartamentoCalculo
{
    /// <summary>Cola aproximada de un par a partir de sus filas del corte (solo fuentes Funcionales).</summary>
    public static int Cola(IEnumerable<(int Total, int Buscados, int Encontrados, int Viejos180)> fuentesFuncionales) =>
        fuentesFuncionales.Sum(f => Math.Max(0, f.Total - f.Buscados)
                                  + Math.Max(0, Math.Min(f.Buscados - f.Encontrados, f.Viejos180)));

    /// <summary>
    /// Agrega los lotes de un grupo. % de cola enviada = filas enviadas (titular × fuente) / cola aproximada,
    /// con tope en 100 (la cola es un techo: el % es un piso). % retorno = buscados tras el envío / enviados.
    /// Días al retorno: promedio de los lotes que ya tuvieron retorno.
    /// </summary>
    public static UsoAgregado Agregar(IReadOnlyCollection<UsoLote> lotes, int? cola)
    {
        var m = MetricasCrudasEnvio.Sumar(lotes.Select(l => l.Metricas));
        var conRetorno = lotes.Where(l => l.DiasRetorno is not null).ToList();
        decimal? pctCola = cola is > 0
            ? Math.Min(100m, CalculoLocalizacion.Pct(m.Titulares, cola.Value) ?? 0)
            : null;
        return new UsoAgregado(
            lotes.Count,
            m,
            cola,
            pctCola,
            CalculoLocalizacion.Pct(m.Buscados, m.Titulares),
            CalculoLocalizacion.Pct(m.Localizados, m.Titulares),
            conRetorno.Count,
            conRetorno.Count == 0
                ? null
                : Math.Round((decimal)conRetorno.Average(l => l.DiasRetorno!.Value), 1, MidpointRounding.AwayFromZero));
    }
}

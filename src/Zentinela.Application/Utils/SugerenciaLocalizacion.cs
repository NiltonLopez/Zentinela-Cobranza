using System.Globalization;
using System.Text;
using Zentinela.Application.Config;
using Zentinela.Application.Settings;

namespace Zentinela.Application.Utils;

/// <summary>
/// Por qué un titular es candidato a buscarse en una fuente (ADR-040, etapa 2). El código de texto es el
/// que guarda <c>LocalizacionEnvioDetalle.Categoria</c> (CHECK del script).
/// </summary>
public enum CategoriaSugerencia
{
    /// <summary>Sin ninguna fila en la fuente ("Falta por buscar" del Calificador).</summary>
    NuncaBuscado = 1,
    /// <summary>La última fila no cumple el predicado de "encontrado" y es anterior al umbral.</summary>
    NoEncontradoViejo = 2,
    /// <summary>La última fila cumple el predicado pero es anterior al umbral de refresco (opcional).</summary>
    EncontradoViejo = 3,
}

/// <summary>Por qué un candidato NO se sugiere. El orden del enum es el orden de precedencia.</summary>
public enum MotivoExclusionSugerencia
{
    /// <summary>UltimoPerfil o MejorPerfil de paz y salvo / ya pagó (<see cref="PerfilClasificacion.YaPago"/>).</summary>
    YaPago = 1,
    /// <summary>Fallecido / Cliente fallecido.</summary>
    Fallecido,
    /// <summary>No gestionar.</summary>
    NoGestionar,
    /// <summary>PQR o reclamación / derecho de petición.</summary>
    Reclamacion,
    /// <summary>Acuerdo o compromiso de pago (<see cref="PerfilClasificacion.Acuerdos"/>).</summary>
    Acuerdo,
    /// <summary>FechaPromesa de hoy en adelante.</summary>
    PromesaVigente,
    /// <summary>Ya va en un lote ENVIADO/CERRADO de la misma fuente dentro de la ventana.</summary>
    EnviadoReciente,
    /// <summary>"Solo con saldo" y el titular no tiene saldo en el par.</summary>
    SinSaldo,
}

/// <summary>Qué dice el UltimoPerfil para el puntaje.</summary>
public enum GrupoPerfilSugerencia
{
    /// <summary>Hubo contacto, tercero u otro perfil: no suma.</summary>
    Otro = 0,
    /// <summary>Ilocalizado, número equivocado / errado / fuera de servicio, "datos de localización".</summary>
    SinDatos,
    /// <summary>Otra marcación sin contacto.</summary>
    SinContacto,
    /// <summary>"SIN GESTION" o sin perfil.</summary>
    SinGestion,
}

/// <summary>Resultado de clasificar un texto de perfil.</summary>
public readonly record struct ClasificacionPerfil(MotivoExclusionSugerencia? Exclusion, GrupoPerfilSugerencia Grupo);

/// <summary>
/// Reglas de perfil de las sugerencias de localización. Los conjuntos salen de
/// <see cref="PerfilClasificacion"/> (ADR-029), con la comparación normalizada: sin tildes, sin distinguir
/// mayúsculas y con los espacios colapsados, porque en <c>UltimoEstadoTitular</c> el mismo perfil aparece
/// como "Datos Localizacion", "DATOS DE LOCALIZACION", "Pago  reflejado" (doble espacio)…
///
/// El servidor lee los perfiles DISTINTOS del par, los clasifica aquí y se los pasa al SQL como JSON:
/// la regla vive en un solo lugar (este) y el SQL solo cruza.
/// </summary>
public static class PerfilesSugerencia
{
    /// <summary>Mayúsculas, sin tildes, espacios colapsados y recortados. Null o vacío → "".</summary>
    public static string Normalizar(string? perfil)
    {
        if (string.IsNullOrWhiteSpace(perfil)) return string.Empty;
        var sb = new StringBuilder(perfil.Length);
        var espacio = false;
        foreach (var c in perfil.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!espacio) sb.Append(' ');
                espacio = true;
                continue;
            }
            espacio = false;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static HashSet<string> Norm(IEnumerable<string> valores) =>
        valores.Select(Normalizar).Where(v => v.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> YaPago    = Norm(PerfilClasificacion.YaPago);
    private static readonly HashSet<string> Acuerdos  = Norm(PerfilClasificacion.Acuerdos);
    private static readonly HashSet<string> SinContactoTodos = Norm(PerfilClasificacion.SinContacto);
    private static readonly HashSet<string> SinGestion = Norm(PerfilClasificacion.SinGestion);

    /// <summary>Fallecido, Cliente Fallecido (del conjunto SinContacto).</summary>
    private static readonly HashSet<string> Fallecido =
        SinContactoTodos.Where(p => p.Contains("FALLECIDO", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

    /// <summary>"No gestionar" (del conjunto ContactoDirecto).</summary>
    private static readonly HashSet<string> NoGestionar =
        Norm(PerfilClasificacion.ContactoDirecto).Where(p => p == "NO GESTIONAR").ToHashSet(StringComparer.Ordinal);

    /// <summary>PQR (ContactoDirecto) y "Reclamación o derecho de petición" (SinContacto).</summary>
    private static readonly HashSet<string> Reclamacion =
        Norm(PerfilClasificacion.ContactoDirecto).Concat(SinContactoTodos)
            .Where(p => p == "PQR" || p.StartsWith("RECLAMACION", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Perfiles del conjunto SinContacto que dicen que el dato de contacto está mal o falta: ilocalizado
    /// (total / parcial), número equivocado, línea errada, fuera de servicio y "datos de localización".
    /// </summary>
    private static readonly HashSet<string> SinDatos =
        SinContactoTodos.Where(p =>
                p.Contains("ILOCALIZADO", StringComparison.Ordinal)
             || p.Contains("EQUIVOCADO", StringComparison.Ordinal)
             || p.Contains("ERRADA", StringComparison.Ordinal)
             || p.Contains("FUERA DE SERVICIO", StringComparison.Ordinal)
             || (p.StartsWith("DATOS", StringComparison.Ordinal) && p.Contains("LOCALIZACION", StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Perfiles que excluyen al titular de las sugerencias, con su motivo (para documentar y testear).</summary>
    public static IReadOnlyDictionary<MotivoExclusionSugerencia, IReadOnlyCollection<string>> Exclusiones { get; } =
        new Dictionary<MotivoExclusionSugerencia, IReadOnlyCollection<string>>
        {
            [MotivoExclusionSugerencia.YaPago]      = YaPago,
            [MotivoExclusionSugerencia.Fallecido]   = Fallecido,
            [MotivoExclusionSugerencia.NoGestionar] = NoGestionar,
            [MotivoExclusionSugerencia.Reclamacion] = Reclamacion,
            [MotivoExclusionSugerencia.Acuerdo]     = Acuerdos,
        };

    /// <summary>Perfiles que suman el puntaje alto ("no tenemos cómo ubicarlo").</summary>
    public static IReadOnlyCollection<string> PerfilesSinDatos => SinDatos;

    /// <summary>
    /// Clasifica un UltimoPerfil: si excluye (y por qué) y a qué grupo del puntaje pertenece. La
    /// precedencia de las exclusiones es la del enum (ya pagó antes que fallecido, etc.).
    /// </summary>
    public static ClasificacionPerfil Clasificar(string? ultimoPerfil)
    {
        var p = Normalizar(ultimoPerfil);

        foreach (var (motivo, conjunto) in Exclusiones.OrderBy(e => e.Key))
            if (conjunto.Contains(p)) return new(motivo, GrupoPerfilSugerencia.Otro);

        if (p.Length == 0 || SinGestion.Contains(p)) return new(null, GrupoPerfilSugerencia.SinGestion);
        if (SinDatos.Contains(p)) return new(null, GrupoPerfilSugerencia.SinDatos);
        if (SinContactoTodos.Contains(p)) return new(null, GrupoPerfilSugerencia.SinContacto);
        return new(null, GrupoPerfilSugerencia.Otro);
    }

    /// <summary>MejorPerfil de paz y salvo / ya pagó: excluye aunque el UltimoPerfil sea otro (ADR-029, regla 1).</summary>
    public static bool EsYaPago(string? perfil) => YaPago.Contains(Normalizar(perfil));

    /// <summary>
    /// La clasificación de todos los perfiles conocidos (los de <see cref="PerfilClasificacion"/>) y de ""
    /// (sin perfil), con clave normalizada, para cruzarla en SQL. Un perfil que no está aquí no excluye ni
    /// suma, igual que en <see cref="Clasificar"/>.
    /// </summary>
    public static IReadOnlyList<(string Clave, MotivoExclusionSugerencia? Exclusion, int Puntos, bool YaPago)> Tabla(
        SugerenciasLocalizacionSettings pesos) =>
        PerfilClasificacion.YaPago
            .Concat(PerfilClasificacion.Acuerdos)
            .Concat(PerfilClasificacion.ContactoDirecto)
            .Concat(PerfilClasificacion.SinContacto)
            .Concat(PerfilClasificacion.SinGestion)
            .Append(string.Empty)
            .Select(Normalizar)
            .Distinct(StringComparer.Ordinal)
            .Select(clave =>
            {
                var c = Clasificar(clave);
                return (clave, c.Exclusion, Puntos(c.Grupo, pesos), YaPago.Contains(clave));
            })
            .Where(x => x.Exclusion is not null || x.Item3 != 0 || x.Item4)
            .ToList();

    /// <summary>Puntos del grupo de perfil según los pesos.</summary>
    public static int Puntos(GrupoPerfilSugerencia grupo, SugerenciasLocalizacionSettings pesos) => grupo switch
    {
        GrupoPerfilSugerencia.SinDatos    => pesos.PuntosPerfilSinDatos,
        GrupoPerfilSugerencia.SinContacto => pesos.PuntosPerfilSinContacto,
        GrupoPerfilSugerencia.SinGestion  => pesos.PuntosPerfilSinContacto,
        _                                 => 0,
    };

    // ── Códigos y etiquetas ──────────────────────────────────────────────────

    public static string Codigo(CategoriaSugerencia c) => c switch
    {
        CategoriaSugerencia.NuncaBuscado      => "NUNCA_BUSCADO",
        CategoriaSugerencia.NoEncontradoViejo => "NO_ENC_VIEJO",
        _                                     => "ENC_VIEJO",
    };

    public static string Etiqueta(CategoriaSugerencia c) => c switch
    {
        CategoriaSugerencia.NuncaBuscado      => "Nunca buscado",
        CategoriaSugerencia.NoEncontradoViejo => "No encontrado y viejo",
        _                                     => "Encontrado pero viejo",
    };

    public static string Codigo(MotivoExclusionSugerencia m) => m switch
    {
        MotivoExclusionSugerencia.YaPago          => "YA_PAGO",
        MotivoExclusionSugerencia.Fallecido       => "FALLECIDO",
        MotivoExclusionSugerencia.NoGestionar     => "NO_GESTIONAR",
        MotivoExclusionSugerencia.Reclamacion     => "RECLAMACION",
        MotivoExclusionSugerencia.Acuerdo         => "ACUERDO",
        MotivoExclusionSugerencia.PromesaVigente  => "PROMESA",
        MotivoExclusionSugerencia.EnviadoReciente => "ENVIADO",
        _                                         => "SIN_SALDO",
    };

    public static MotivoExclusionSugerencia? MotivoDesdeCodigo(string? codigo) =>
        Enum.GetValues<MotivoExclusionSugerencia>().Cast<MotivoExclusionSugerencia?>()
            .FirstOrDefault(m => Codigo(m!.Value) == codigo);

    public static string Etiqueta(MotivoExclusionSugerencia m) => m switch
    {
        MotivoExclusionSugerencia.YaPago          => "Paz y salvo / ya pagó",
        MotivoExclusionSugerencia.Fallecido       => "Fallecido",
        MotivoExclusionSugerencia.NoGestionar     => "No gestionar",
        MotivoExclusionSugerencia.Reclamacion     => "PQR / reclamación",
        MotivoExclusionSugerencia.Acuerdo         => "Acuerdo o compromiso de pago",
        MotivoExclusionSugerencia.PromesaVigente  => "Promesa vigente",
        MotivoExclusionSugerencia.EnviadoReciente => "Ya enviado a esa fuente",
        _                                         => "Sin saldo",
    };
}

/// <summary>Lo que el puntaje necesita saber de un candidato (titular + fuente).</summary>
public sealed record EntradaPrioridad(
    GrupoPerfilSugerencia GrupoPerfil,
    decimal? Saldo,
    decimal? P90Saldo,
    DateOnly? FechaUltimaGestion,
    bool BuscadoEnAlgunaFuncional,
    CategoriaSugerencia Categoria,
    bool ContactoFresco,
    bool IncluirLocalizados);

/// <summary>Desglose explicable del puntaje: cada componente por separado y el total.</summary>
public sealed record DesglosePrioridad(
    int Perfil, int Saldo, int Gestion, int NuncaFuncional, int NuncaFuente, int NoEncontradoViejo, int ContactoFresco)
{
    public int Total => Perfil + Saldo + Gestion + NuncaFuncional + NuncaFuente + NoEncontradoViejo + ContactoFresco;
}

/// <summary>
/// Puntaje de una sugerencia (ADR-040). El SQL de <c>GeneradorSqlSugerencias</c> calcula EXACTAMENTE lo
/// mismo para ordenar el top sobre todos los candidatos; el servidor recalcula con esto el desglose de
/// las filas devueltas y avisa en el log si alguna no coincide.
/// </summary>
public static class PrioridadSugerencia
{
    public static DesglosePrioridad Calcular(EntradaPrioridad e, SugerenciasLocalizacionSettings pesos, DateOnly hoy)
    {
        var perfil = PerfilesSugerencia.Puntos(e.GrupoPerfil, pesos);

        // Saldo relativo al P90 del par, con tope en 1; sin P90 (par sin saldo) no suma.
        var saldo = 0;
        if (e.P90Saldo is > 0 && e.Saldo is > 0)
        {
            var proporcion = Math.Min(1m, e.Saldo.Value / e.P90Saldo.Value);
            saldo = (int)Math.Round(pesos.PuntosSaldoMax * proporcion, 0, MidpointRounding.AwayFromZero);
        }

        var dias = e.FechaUltimaGestion is { } f ? hoy.DayNumber - f.DayNumber : (int?)null;
        var gestion = dias is null || dias > 90 ? pesos.PuntosGestionMas90
                    : dias > 30 ? pesos.PuntosGestionMas30
                    : 0;

        return new DesglosePrioridad(
            Perfil: perfil,
            Saldo: saldo,
            Gestion: gestion,
            NuncaFuncional: e.BuscadoEnAlgunaFuncional ? 0 : pesos.PuntosNuncaFuncional,
            NuncaFuente: e.Categoria == CategoriaSugerencia.NuncaBuscado ? pesos.PuntosNuncaFuente : 0,
            NoEncontradoViejo: e.Categoria == CategoriaSugerencia.NoEncontradoViejo ? pesos.PuntosNoEncontradoViejo : 0,
            ContactoFresco: e.ContactoFresco && !e.IncluirLocalizados ? -pesos.PenalizacionContactoFresco : 0);
    }

    /// <summary>
    /// Motivo por el que un candidato se excluye, con la misma precedencia que el SQL: perfil (MejorPerfil
    /// ya pagó incluido), promesa vigente, enviado reciente y, al final, sin saldo. Null = se sugiere.
    /// </summary>
    public static MotivoExclusionSugerencia? Excluir(
        string? ultimoPerfil, string? mejorPerfil, DateOnly? fechaPromesa, decimal? saldo,
        bool enviadoReciente, bool soloConSaldo, DateOnly hoy)
    {
        if (PerfilesSugerencia.EsYaPago(mejorPerfil)) return MotivoExclusionSugerencia.YaPago;
        if (PerfilesSugerencia.Clasificar(ultimoPerfil).Exclusion is { } m) return m;
        if (fechaPromesa is { } p && p >= hoy) return MotivoExclusionSugerencia.PromesaVigente;
        if (enviadoReciente) return MotivoExclusionSugerencia.EnviadoReciente;
        if (soloConSaldo && !(saldo > 0)) return MotivoExclusionSugerencia.SinSaldo;
        return null;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zentinela.Application;

/// <summary>
/// Reglas de negocio del Creador de Virtualidad (SMS), en un solo lugar para que el
/// front no las duplique y para que validar, anotar el catálogo y generar coincidan
/// por construcción (ADR-037).
/// </summary>
public static class CatalogoCreadorVirtualidad
{
    /// <summary>Largo máximo de un SMS en GSM-7. Con un carácter más el operador parte el mensaje.</summary>
    public const int LimiteSms = 160;

    /// <summary>
    /// Hasta este número de titulares que exceden el límite, el mensaje sigue disponible
    /// con advertencia; por encima queda deshabilitado.
    /// </summary>
    public const int UmbralAdvertencia = 10;

    /// <summary>Celular colombiano: siempre 10 dígitos. Se usa para el peor caso del cálculo.</summary>
    public const int LongitudCelular = 10;

    /// <summary>Filas excedidas que viajan al front por mensaje (para listar "cuáles").</summary>
    public const int MaxExcedidosEnRespuesta = 200;

    /// <summary>Filas limpias que se muestran como muestra en el preview.</summary>
    public const int FilasMuestra = 10;

    // ── Placeholders ────────────────────────────────────────────────────────

    public const string PhNombre        = "{NOMBRE}";
    public const string PhCelularAsesor = "{CELULAR_ASESOR}";
    public const string PhLinkAsesor    = "{LINK_ASESOR}";
    public const string PhCartera       = "{CARTERA}";
    public const string PhEmpresa       = "{EMPRESA}";

    public static readonly IReadOnlyList<string> PlaceholdersPermitidos =
        [PhNombre, PhCartera, PhEmpresa, PhCelularAsesor, PhLinkAsesor];

    /// <summary>Placeholders que dependen de una columna OPCIONAL del archivo (Cartera, Empresa).</summary>
    public static readonly IReadOnlyDictionary<string, string> PlaceholderPorColumnaOpcional =
        new Dictionary<string, string> { [PhCartera] = "Cartera", [PhEmpresa] = "Empresa" };

    public static bool UsaPlaceholder(string texto, string placeholder) =>
        texto.Contains(placeholder, StringComparison.Ordinal);

    /// <summary>Los que todo mensaje DEBE traer: sin ellos el titular no tiene a quién responder.</summary>
    public static readonly IReadOnlyList<string> PlaceholdersObligatorios =
        [PhCelularAsesor, PhLinkAsesor];

    /// <summary>Cualquier {…} del texto, para detectar placeholders desconocidos.</summary>
    public static readonly Regex CualquierPlaceholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    // ── Alfabeto GSM-7 ──────────────────────────────────────────────────────

    /// <summary>
    /// Alfabeto básico GSM 03.38 más la extensión. Un solo carácter fuera de él (una tilde,
    /// una ñ) fuerza UCS-2 y el límite real cae a 70. Por eso el catálogo rechaza todo lo
    /// que no esté aquí. Las llaves { } están en la extensión, pero SOLO se aceptan como
    /// parte de un placeholder conocido (ver <see cref="CaracteresNoGsm7"/>).
    /// </summary>
    private static readonly HashSet<char> AlfabetoGsm7 = new(
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà" +
        "\f^{}\\[~]|€");

    /// <summary>
    /// Caracteres del texto que NO pueden ir en un SMS del catálogo, con su posición
    /// (base 1), para que el formulario los marque. Además de lo que no está en GSM-7,
    /// se rechazan explícitamente ñ, Ñ, tildes y los caracteres de la extensión
    /// (cuentan doble) — salvo las llaves cuando forman un placeholder permitido.
    /// </summary>
    public static List<(char Caracter, int Posicion)> CaracteresNoGsm7(string texto)
    {
        var rechazados = new List<(char, int)>();
        if (string.IsNullOrEmpty(texto)) return rechazados;

        // Posiciones cubiertas por placeholders permitidos: sus llaves sí valen.
        var enPlaceholder = new bool[texto.Length];
        foreach (Match m in CualquierPlaceholder.Matches(texto))
        {
            if (!PlaceholdersPermitidos.Contains(m.Value)) continue;
            for (var i = m.Index; i < m.Index + m.Length; i++) enPlaceholder[i] = true;
        }

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (enPlaceholder[i]) continue;

            var esExtension = c is '{' or '}' or '[' or ']' or '~' or '\\' or '^' or '|' or '€' or '\f';
            var esAcentuada = c is 'ñ' or 'Ñ' or 'á' or 'é' or 'í' or 'ó' or 'ú' or 'Á' or 'É' or 'Í' or 'Ó' or 'Ú'
                                or 'ü' or 'Ü' or 'à' or 'è' or 'ì' or 'ò' or 'ù' or 'ä' or 'ö' or 'Ä' or 'Ö';

            if (!AlfabetoGsm7.Contains(c) || esExtension || esAcentuada)
                rechazados.Add((c, i + 1));
        }
        return rechazados;
    }

    // ── Perfiles que no se pueden contactar ─────────────────────────────────

    /// <summary>
    /// Titulares con alguno de estos perfiles — en el archivo o en cualquier registro de
    /// HistoricoEstadoTitular — no reciben SMS. Se comparan sin mayúsculas ni tildes
    /// (en BD conviven "No gestionar" y "NO GESTIONAR"). Se pasan como parámetros con
    /// COLLATE Latin1_General_CI_AI, no concatenados.
    /// </summary>
    public static readonly IReadOnlyList<string> PerfilesExcluidosSms =
    [
        "No gestionar",
        "PQR",
        "Paz y salvo",
        "Ya pago",
        "Reclamacion o derecho de peticion",
        "Fallecido",
        "Cliente fallecido",
    ];

    /// <summary>Los mismos perfiles ya normalizados, para comparar el valor del archivo en memoria.</summary>
    public static readonly HashSet<string> PerfilesExcluidosClave =
        PerfilesExcluidosSms.Select(ClaveComparacion).ToHashSet(StringComparer.Ordinal);

    // ── Columnas del archivo de entrada ─────────────────────────────────────

    /// <summary>
    /// Encabezados de la plantilla, en orden. Las 5 primeras son obligatorias; Cartera y
    /// Empresa son OPCIONALES: si faltan, los mensajes que usen {CARTERA} o {EMPRESA} quedan
    /// deshabilitados con ese motivo (no se adivina ni se deja vacío).
    /// </summary>
    public static readonly string[] ColumnasPlantilla =
        ["Cédula", "Nombre", "Fecha Gestión", "Ultimo Perfil", "Numero Marcado", "Cartera", "Empresa"];

    public static readonly string[] ColumnasOpcionales = ["Cartera", "Empresa"];

    /// <summary>Columnas del archivo que se entrega a virtualidad.</summary>
    public static readonly string[] ColumnasSalida =
        ["Cédula", "Nombre", "Fecha Gestión", "Ultimo Perfil", "Numero Marcado", "Cartera", "Empresa",
         "Celular Asesor", "Link Asesor", "Mensaje"];

    /// <summary>
    /// Alias aceptados por columna, ya normalizados (sin tildes, minúsculas, sin espacios
    /// ni guiones). No se adivina: si falta una, se rechaza el archivo diciendo cuál.
    /// </summary>
    public static class AliasSms
    {
        public static readonly string[] Cedula        = ["cedula", "numerodocumento", "documento", "identificacion", "nrodocumento", "titularid"];
        public static readonly string[] Nombre        = ["nombre", "nombres", "nombrecompleto", "nombretitular", "titular"];
        public static readonly string[] FechaGestion  = ["fechagestion", "fechadegestion", "fechaultimagestion", "ultimagestion", "fecha"];
        public static readonly string[] UltimoPerfil  = ["ultimoperfil", "perfil", "ultimoestado", "estado", "tipificacion"];
        public static readonly string[] NumeroMarcado = ["numeromarcado", "numero", "celular", "telefono", "numerotelefono", "numerocelular", "movil", "destino"];
        public static readonly string[] Cartera       = ["cartera", "nombrecartera", "portafolio"];
        public static readonly string[] Empresa       = ["empresa", "nombreempresa", "entidad", "cliente", "acreedor"];
    }

    // ── Cálculo de longitud ─────────────────────────────────────────────────

    /// <summary>Reemplaza los cinco placeholders. Es el ÚNICO lugar donde se arma un SMS.</summary>
    public static string ArmarMensaje(string plantilla, string nombre, string cartera, string empresa, string celularAsesor, string linkAsesor) =>
        plantilla
            .Replace(PhNombre, nombre, StringComparison.Ordinal)
            .Replace(PhCartera, cartera, StringComparison.Ordinal)
            .Replace(PhEmpresa, empresa, StringComparison.Ordinal)
            .Replace(PhCelularAsesor, celularAsesor, StringComparison.Ordinal)
            .Replace(PhLinkAsesor, linkAsesor, StringComparison.Ordinal);

    /// <summary>
    /// Longitud del SMS para este titular en el PEOR caso: celular de 10 dígitos y el link
    /// más largo de los asesores. Como el asesor se sortea después, calcular con el peor
    /// link garantiza que ningún mensaje final supere el límite y que "se pasa en N
    /// titulares" del paso 3 sea exactamente el N que se borra en el paso 4.
    /// </summary>
    public static int LongitudPeorCaso(string plantilla, string nombre, string cartera, string empresa, string linkMasLargo) =>
        ArmarMensaje(plantilla, nombre, cartera, empresa, new string('9', LongitudCelular), linkMasLargo).Length;

    /// <summary>Longitud del texto sin ningún placeholder (lo que ocupa el mensaje "fijo").</summary>
    public static int LongitudBase(string plantilla) =>
        ArmarMensaje(plantilla, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty).Length;

    // ── Normalización para comparar ─────────────────────────────────────────

    /// <summary>
    /// Sin tildes, minúsculas, espacios colapsados. Para comparar perfiles del archivo con
    /// <see cref="PerfilesExcluidosClave"/>. Distinto de la normalización de encabezados
    /// (que quita los espacios) y de la de nombres (que deja mayúsculas).
    /// </summary>
    public static string ClaveComparacion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sinMarcas = new string(s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        return Regex.Replace(sinMarcas.ToLowerInvariant(), @"\s+", " ").Trim();
    }
}

using System.Globalization;
using System.Text;

namespace Zentinela.Application;

/// <summary>Cómo se carga un canal de virtualidad.</summary>
public enum ModoCargaCanal
{
    /// <summary>Solicitud primero (Estado null) y Resultado después, por PK. SMS y Correo.</summary>
    DosFases = 0,

    /// <summary>Un solo archivo que ya trae el resultado de cada evento. IVR.</summary>
    ArchivoUnico = 1,
}

/// <summary>
/// Catálogo de virtualidad: modos de carga por canal y traducción de los códigos crudos
/// del marcador. Fuente ÚNICA de verdad — el frontend la consume por
/// <c>GET /api/virtualidad/historico/catalogo</c> en vez de duplicarla (ver ADR-031).
/// </summary>
public static class CatalogoVirtualidad
{
    public const string CanalIvr    = "IVR";
    public const string CanalSms    = "SMS";
    public const string CanalCorreo = "Correo";

    /// <summary>
    /// IVR: código de la columna "Last App" del marcador (Asterisk/Issabel) → Estado almacenado.
    ///
    /// Dos traducciones se apartan a propósito del código crudo:
    ///   - <c>BackGround</c> → OPCION INCORRECTA (no "ERROR"): el cliente contestó e interactuó;
    ///     llamarlo error lo mezclaría con ERROR PROCESO y ERROR SISTEMA, que sí son fallas
    ///     técnicas, y se perdería que hubo contacto real.
    ///   - <c>Dial</c> → OPCION 1 (no "MARCAR"): es el estado de éxito, y "marcar" se lee como
    ///     "se discó la llamada", justo lo contrario de lo que significa.
    /// </summary>
    public static readonly IReadOnlyList<(string Codigo, string Estado, string Significado)> IvrLastApp =
    [
        ("AGI",            "ERROR PROCESO",     "Error durante la ejecución de la llamada, no continuó"),
        ("BackGround",     "OPCION INCORRECTA", "Marcaron la opción que no era"),
        ("Dial",           "OPCION 1",          "Escucharon el audio y marcaron opción 1"),
        ("Hangup",         "COLGAR",            "Contestó y colgó la llamada"),
        ("MixMonitor",     "ERROR SISTEMA",     "Falló el proceso de grabación"),
        ("Playback",       "REPRODUCIR",        "Escucharon el audio y colgaron"),
        ("Set",            "PROCESO INTERNO",   "Se ejecutó una configuración interna del sistema, no procesó la llamada"),
        ("StopMixMonitor", "FIN GRABACION",     "Finalizó el proceso de grabación"),
        ("Wait",           "ESCUCHANDO",        "Escuchó el mensaje y permaneció en llamada un tiempo muy corto"),
        ("WaitExten",      "ESPERAR OPCION",    "Escucharon el audio y se quedaron a la espera sin marcar opción 1"),
        ("Congestion",     "CONGESTIONADO",     "El operador o la red no procesó la llamada"),
    ];

    /// <summary>Código normalizado → Estado. Permite que el archivo traiga cualquier casing.</summary>
    public static readonly IReadOnlyDictionary<string, string> IvrEstadoPorCodigo =
        IvrLastApp.ToDictionary(x => NormalizarCodigo(x.Codigo), x => x.Estado, StringComparer.Ordinal);

    /// <summary>Estados válidos por canal. IVR sale del catálogo de traducción.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> EstadosPorCanal =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [CanalIvr]    = IvrLastApp.Select(x => x.Estado).ToArray(),
            [CanalSms]    = ["Entregado", "No entregado"],
            [CanalCorreo] = ["Entregado", "Rebotado", "Abierto"],
        };

    /// <summary>Encabezados aceptados por campo, ya normalizados.</summary>
    public static class AliasIvr
    {
        public static readonly string[] Cartera  = ["cartera", "nombrecartera"];
        public static readonly string[] Lider    = ["lider", "nombrelider", "liderasignado"];
        public static readonly string[] Fecha    = ["fechamarcado", "fechademarcado", "fechaenvio", "fecha"];
        public static readonly string[] Numero   = ["numero", "numerotelefono", "telefono", "celular", "destino"];
        public static readonly string[] LastApp  = ["lastapp", "ultimaapp", "aplicacion", "app"];
        public static readonly string[] Documento = ["cedula", "numerodocumento", "documento", "identificacion", "nrodocumento"];
    }

    /// <summary>Etiquetas legibles de las columnas que el archivo IVR debe traer.</summary>
    public static readonly string[] ColumnasIvr =
        ["Cartera", "Lider", "Fecha Marcado", "Número", "Last App", "Cedula"];

    // ── Desenlaces del análisis IVR ─────────────────────────────────────────

    /// <summary>
    /// Los 11 estados se agrupan en 5 desenlaces de negocio, ordenados de mejor a peor.
    /// El <c>Rango</c> es la jerarquía con la que se resuelve un cliente marcado varias
    /// veces: se le atribuye su MEJOR desenlace, y así cuenta una sola vez.
    /// El rango <see cref="RangoSinContacto"/> agrupa los estados que no implican contacto
    /// (fallas técnicas y pasos internos del marcador): quedan fuera de "contactados".
    /// </summary>
    public const int RangoSinContacto = 99;

    /// <summary>
    /// Cliente sin resultado cargado (Estado NULL). Se separa de <see cref="RangoSinContacto"/>
    /// a propósito: "no contestó" y "todavía no sabemos qué pasó" son cosas distintas, y
    /// mezclarlas haría ver como contactabilidad pésima lo que en realidad es data faltante.
    /// Es el caso de las filas cargadas por el flujo de dos fases que nunca recibieron resultado.
    /// </summary>
    public const int RangoSinResultado = 98;

    public static readonly IReadOnlyList<(int Rango, string Desenlace, string[] Estados)> DesenlacesIvr =
    [
        (1, "Marcaron opción 1 (transferidos)",             ["OPCION 1"]),
        (2, "Escucharon el menú y no marcaron",             ["ESPERAR OPCION"]),
        (3, "Audio con opción activa, sin marcación válida", ["OPCION INCORRECTA"]),
        (4, "Solo alcanzaron a oír el audio",               ["REPRODUCIR", "ESCUCHANDO"]),
        (5, "Colgaron",                                      ["COLGAR"]),
    ];

    /// <summary>Estado → rango del desenlace. Los no listados son <see cref="RangoSinContacto"/>.</summary>
    public static readonly IReadOnlyDictionary<string, int> RangoPorEstado =
        DesenlacesIvr
            .SelectMany(d => d.Estados.Select(e => (Estado: e, d.Rango)))
            .ToDictionary(x => x.Estado, x => x.Rango, StringComparer.Ordinal);

    public static string NombreDesenlace(int rango) =>
        DesenlacesIvr.FirstOrDefault(d => d.Rango == rango).Desenlace ?? "Sin contacto";

    /// <summary>Rangos que cuentan como "escuchó el menú" (llegó a oír las opciones).</summary>
    public static readonly int[] RangosEscuchoMenu = [1, 2, 3];

    public static bool EsCanalValido(string? canal) =>
        !string.IsNullOrWhiteSpace(canal) && EstadosPorCanal.ContainsKey(canal);

    public static bool EsIvr(string? canal) =>
        string.Equals(canal, CanalIvr, StringComparison.OrdinalIgnoreCase);

    public static ModoCargaCanal ModoDe(string canal) =>
        EsIvr(canal) ? ModoCargaCanal.ArchivoUnico : ModoCargaCanal.DosFases;

    /// <summary>
    /// Normaliza un código de "Last App" para buscarlo: sin tildes, sin espacios ni separadores,
    /// en minúsculas. Así "StopMixMonitor", "stopmixmonitor" y "Stop Mix Monitor" son el mismo.
    /// Duplica a propósito la lógica de ArchivoTabular.Normalizar: Application no puede depender
    /// de Infrastructure.
    /// </summary>
    public static string NormalizarCodigo(string s)
    {
        var sinTildes = (s ?? string.Empty).Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(sinTildes)
            .ToLowerInvariant()
            .Replace(" ", "").Replace("_", "").Replace("-", "")
            .Replace("*", "").Replace("(", "").Replace(")", "")
            .Trim();
    }
}

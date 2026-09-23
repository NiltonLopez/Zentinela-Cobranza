namespace Zentinela.Application;

/// <summary>Cómo se limpia y valida una columna de dato de una fuente.</summary>
public enum TipoColumnaCargue
{
    /// <summary>Texto libre: trim, espacios colapsados, vacíos a NULL, truncado a <c>Largo</c>.</summary>
    Texto,
    /// <summary>Celular colombiano: 10 dígitos que empiezan en 3 (se quita el 57).</summary>
    Celular,
    /// <summary>Fijo colombiano: 10 dígitos que empiezan en 6.</summary>
    Fijo,
    /// <summary>Cualquiera de los dos anteriores (columnas que mezclan celular y fijo).</summary>
    Telefono,
    /// <summary>Correo: forma mínima + lista negra por igualdad exacta.</summary>
    Email,
    /// <summary>Número entero (bigint).</summary>
    Entero,
    /// <summary>Fecha (date).</summary>
    Fecha,
    /// <summary>Placa de vehículo: mayúsculas, sin espacios.</summary>
    Placa,
}

/// <summary>Cuántas filas puede tener un titular en la tabla destino.</summary>
public enum CardinalidadCargue
{
    /// <summary>Un titular = una fila de contenido vigente (Sura, Cajacopi…).</summary>
    UnaPorTitular,
    /// <summary>Un titular = N filas distintas (SIMIT: una por placa; DatosClaro: una por teléfono).</summary>
    VariasPorTitular,
}

/// <summary>Tipo SQL de la columna de sello (<c>FechaConsulta</c> / <c>FechaInsercion</c>) en el DESTINO.</summary>
public enum TipoFechaSello { Date, DateTime }

/// <summary>
/// Columna de dato de una fuente. <paramref name="Nombre"/> es el nombre EXACTO de la columna
/// en la tabla destino: se usa como identificador SQL, por eso solo puede venir de este
/// catálogo (código), jamás del archivo ni del usuario.
/// </summary>
/// <param name="Alias">Encabezados aceptados, ya normalizados con <c>ArchivoTabular.Normalizar</c>.</param>
/// <param name="Largo">varchar(n) del destino; 0 para tipos sin largo (Entero, Fecha).</param>
/// <param name="Obligatoria">La columna debe EXISTIR en el archivo (no que cada celda tenga valor).</param>
/// <param name="EntraAlFlag">Si tener valor aquí cuenta para <c>EstadoEn&lt;Fuente&gt;</c>.</param>
/// <param name="EntraAClave">Si participa en la clave de contenido (refrescar vs insertar).</param>
public sealed record ColumnaFuenteCargue(
    string Nombre,
    string[] Alias,
    TipoColumnaCargue Tipo,
    int Largo = 0,
    bool Obligatoria = false,
    bool EntraAlFlag = true,
    bool EntraAClave = true,
    string Descripcion = "");

/// <summary>
/// Todo lo que el motor genérico de cargue necesita saber de una fuente. El motor no tiene
/// NADA específico de una fuente: lo que no se puede expresar aquí va en su regla
/// (<c>Infrastructure/.../CargueFuentes/Reglas</c>).
/// </summary>
public sealed record DefinicionFuenteCargue
{
    /// <summary>Clave de ruta y de catálogo ("Sura").</summary>
    public required string Fuente { get; init; }
    public required string Etiqueta { get; init; }
    /// <summary>"dbo.Sura". Identificador SQL: solo desde código.</summary>
    public required string TablaDestino { get; init; }
    public required IReadOnlyList<ColumnaFuenteCargue> ColumnasDato { get; init; }
    /// <summary><c>EstadoEn&lt;Fuente&gt;</c>; null si la fuente no tiene flag (DatosClaro).</summary>
    public string? ColumnaFlag { get; init; }
    /// <summary>Columna de sello en el destino: <c>FechaConsulta</c> o <c>FechaInsercion</c>.</summary>
    public required string ColumnaFecha { get; init; }
    public TipoFechaSello TipoFecha { get; init; } = TipoFechaSello.Date;
    /// <summary>El destino guarda el TitularID como varchar (NuevaEps).</summary>
    public bool TitularIdComoTexto { get; init; }
    public bool CreaTitular { get; init; } = true;
    public bool CreaTitularCartera { get; init; } = true;
    public CardinalidadCargue Cardinalidad { get; init; } = CardinalidadCargue.UnaPorTitular;
    /// <summary>No entra una fila vacía de un titular con datos, y se purgan sus vacías viejas.</summary>
    public bool AplicaPoliticaVacias { get; init; } = true;
    /// <summary>Columnas que el archivo trae pero no van al destino tal cual (SIMIT Placa1..15).</summary>
    public IReadOnlyList<ColumnaFuenteCargue> ColumnasArchivoExtra { get; init; } = [];
    public bool Habilitada { get; init; } = true;
    public string Notas { get; init; } = "";
    public int FaseIntegracion { get; init; } = 1;
}

/// <summary>
/// Catálogo de fuentes del módulo Cargue de Fuentes de Datos (ADR-039). Vive en Application
/// porque el front lo consume por <c>GET /api/cargue-fuentes/fuentes</c> (mismo precedente que
/// <see cref="CatalogoCreadorVirtualidad"/>): una sola fuente de verdad para columnas y alias.
/// </summary>
public static class CatalogoCargueFuentes
{
    // ── Columnas comunes (las resuelve el motor, no van en ColumnasDato) ────
    // Alias ya normalizados. Si una fuente usa alguno de estos alias en una columna de dato
    // (Sura: "nit" es el NIT del empleador), el motor se lo deja a la columna de dato: una
    // misma columna del archivo nunca alimenta dos cosas.

    public static readonly string[] AliasTitularId = ["titularid", "numerodocumento", "documento", "cedula", "identificacion", "nit"];
    public static readonly string[] AliasNombre    = ["nombre", "nombrecompleto", "nombretitular", "razonsocial"];
    public static readonly string[] AliasCarteraId = ["carteraid", "idcartera"];
    public static readonly string[] AliasCartera   = ["cartera", "nombrecartera"];
    public static readonly string[] AliasLiderId   = ["liderid", "idlider"];
    public static readonly string[] AliasLider     = ["lider", "nombrelider", "liderasignado"];

    public static readonly string[] ExtensionesAceptadas = [".csv", ".xlsx"];

    /// <summary>Filas de muestra del preview.</summary>
    public const int FilasMuestra = 10;

    /// <summary>Mensajes distintos por lista (errores / advertencias / info) que viajan al front.</summary>
    public const int MaxMensajesAgrupados = 200;

    // ── Fuentes ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sura (EPS). Plantilla del motor: no tiene regla propia, todo lo resuelve lo genérico.
    /// Tipos y largos verificados contra <c>sys.columns</c> de <c>dbo.Sura</c> el 2026-09-23
    /// (FechaConsulta es <c>date</c>, NOT NULL).
    /// </summary>
    public static readonly DefinicionFuenteCargue Sura = new()
    {
        Fuente       = "Sura",
        Etiqueta     = "Sura (EPS)",
        TablaDestino = "dbo.Sura",
        ColumnaFlag  = "EstadoEnSura",
        ColumnaFecha = "FechaConsulta",
        TipoFecha    = TipoFechaSello.Date,
        Notas        = "Afiliados EPS Sura: celular, email y empleador.",
        ColumnasDato =
        [
            new("NumeroCelular", ["numerocelular", "celular", "numerotelefonocelular", "telefonocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
            new("NIT", ["nit", "nitempresa", "nitempleador"],
                TipoColumnaCargue.Entero, Descripcion: "NIT de la empresa empleadora"),
            new("Empresa", ["empresa", "nombreempresa", "empleador"],
                TipoColumnaCargue.Texto, 100, Descripcion: "Empresa empleadora"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, Descripcion: "Cotizante, beneficiario…"),
            new("EstadoAfiliacion", ["estadoafiliacion", "estadodeafiliacion"],
                TipoColumnaCargue.Texto, 50, Descripcion: "Activo, retirado…"),
            // Fecha que informa Sura, no la del cargue: no cuenta como "dato de contacto".
            new("UltimaFechaConsulta", ["ultimafechaconsulta", "fechaultimaconsulta"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Última consulta según Sura"),
        ],
    };

    /// <summary>
    /// Cajacopi (EPS). Regla propia: prefijo a los fijos de 7 dígitos, fijos '11111' y correos
    /// "NO TIENE". Tipos y largos verificados contra <c>sys.columns</c> de <c>dbo.Cajacopi</c> el
    /// 2026-09-23 (FechaNacimiento y FechaConsulta son <c>date</c>; FK a Titular, sin triggers).
    /// El flag incluye FechaNacimiento, como el SP legacy; Departamento y Municipio no (son
    /// localización, no contacto).
    /// </summary>
    public static readonly DefinicionFuenteCargue Cajacopi = new()
    {
        Fuente       = "Cajacopi",
        Etiqueta     = "Cajacopi (EPS)",
        TablaDestino = "dbo.Cajacopi",
        ColumnaFlag  = "EstadoEnCajacopi",
        ColumnaFecha = "FechaConsulta",
        TipoFecha    = TipoFechaSello.Date,
        Notas        = "Afiliados Cajacopi: celular, fijo (se prefija si viene de 7 dígitos), email y localización.",
        ColumnasDato =
        [
            new("NumeroTelefonoCelular", ["numerotelefonocelular", "celular", "numerocelular", "telefonocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("NumeroTelefonoFijo", ["numerotelefonofijo", "fijo", "telefonofijo", "numerofijo"],
                TipoColumnaCargue.Fijo, 10, Descripcion: "Fijo (10 dígitos; si viene de 7 se le pone el indicativo)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 100, Descripcion: "Correo electrónico"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Departamento (resuelve el indicativo del fijo)"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio (resuelve el indicativo del fijo)"),
            new("FechaNacimiento", ["fechanacimiento", "fechadenacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, Descripcion: "Fecha de nacimiento"),
        ],
    };

    public static readonly IReadOnlyList<DefinicionFuenteCargue> Todas = [Sura, Cajacopi];

    public static DefinicionFuenteCargue? Buscar(string? fuente) =>
        string.IsNullOrWhiteSpace(fuente)
            ? null
            : Todas.FirstOrDefault(f => f.Fuente.Equals(fuente.Trim(), StringComparison.OrdinalIgnoreCase));
}

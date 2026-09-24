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
    /// <summary>
    /// Número entero: bigint salvo que la columna declare otro <see cref="TipoEnteroSql"/>. Lo que no
    /// cabe en el tipo del destino se anula con advertencia antes de escribir.
    /// </summary>
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
/// Tipo SQL de una columna <see cref="TipoColumnaCargue.Entero"/> en el DESTINO (y en #Carga).
/// Decide el máximo aceptado: un número que no cabe haría fallar el INSERT de TODO el archivo
/// por desbordamiento, así que el motor lo anula por fila con advertencia agrupada.
/// </summary>
public enum TipoEnteroSql
{
    /// <summary>bigint (hasta 9.223.372.036.854.775.807).</summary>
    BigInt,
    /// <summary>int (hasta 2.147.483.647).</summary>
    Int,
}

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
/// <param name="DesdeArchivo">false = la columna no se lee del archivo: la produce la regla
/// (SIMIT: <c>Placa</c> sale de Placa1..15 en <c>Expandir</c>). No se busca, no se pide y no se
/// lista como faltante.</param>
/// <param name="EnteroSql">Solo <see cref="TipoColumnaCargue.Entero"/>: tipo SQL del destino
/// (verificado con <c>sys.columns</c>). Lo que no cabe se anula con advertencia.</param>
/// <param name="EsNit">Solo <see cref="TipoColumnaCargue.Entero"/>: la columna es un NIT, así que
/// un 'NIT-DV' ('900123456-7') se guarda sin el dígito de verificación (aviso informativo) en vez
/// de anularse por no numérico.</param>
public sealed record ColumnaFuenteCargue(
    string Nombre,
    string[] Alias,
    TipoColumnaCargue Tipo,
    int Largo = 0,
    bool Obligatoria = false,
    bool EntraAlFlag = true,
    bool EntraAClave = true,
    string Descripcion = "",
    bool DesdeArchivo = true,
    TipoEnteroSql EnteroSql = TipoEnteroSql.BigInt,
    bool EsNit = false);

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

    /// <summary>
    /// Columna del destino que identifica la fila y contra la que se refresca, inserta y purga
    /// (Fase 2). "TitularID" en todas salvo CamaraEmpresa, que no tiene TitularID: guarda el NIT
    /// de la empresa en <c>NitEmpresa</c>. Una clave que no es TitularID no es un titular: la
    /// fuente no crea Titular ni Titular_Cartera ni se cruza con dbo.Titular (lo exige
    /// <c>GeneradorSqlCargue.ValidarDefinicion</c>). Se valida con la misma regla que un documento
    /// (5 a 15 dígitos) y se escribe en <c>CargueFuenteReversion.TitularID</c> como texto.
    /// </summary>
    public string ColumnaClaveTitular { get; init; } = "TitularID";

    /// <summary>Encabezados de la clave en el archivo; null = <see cref="CatalogoCargueFuentes.AliasTitularId"/>.</summary>
    public string[]? AliasClaveTitular { get; init; }

    /// <summary>Texto del panel de columnas esperadas para la clave.</summary>
    public string DescripcionClaveTitular { get; init; } = "Documento del titular (5 a 15 dígitos)";

    /// <summary>La clave es el documento de un titular de dbo.Titular (todas menos CamaraEmpresa).</summary>
    public bool ClaveEsTitular => string.Equals(ColumnaClaveTitular, "TitularID", StringComparison.OrdinalIgnoreCase);

    /// <summary>Alias efectivos de la clave en el archivo.</summary>
    public string[] AliasDeClave => AliasClaveTitular ?? CatalogoCargueFuentes.AliasTitularId;
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
                TipoColumnaCargue.Entero, EsNit: true, Descripcion: "NIT de la empresa empleadora ('NIT-DV' se acepta: se quita el DV)"),
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

    /// <summary>
    /// MutualSer (EPS). Regla propia: reubicar teléfonos entre celular, celular 2 y fijo.
    /// Verificado contra <c>sys.columns</c> de <c>dbo.MutualSer</c> el 2026-09-23 (FechaConsulta
    /// <c>date</c>; sin FK ni triggers). El flag, como en el SP legacy, solo mira teléfonos y email:
    /// régimen, tipo de afiliado y localización no son datos de contacto.
    /// </summary>
    public static readonly DefinicionFuenteCargue MutualSer = new()
    {
        Fuente       = "MutualSer",
        Etiqueta     = "MutualSer (EPS)",
        TablaDestino = "dbo.MutualSer",
        ColumnaFlag  = "EstadoEnMutualSer",
        ColumnaFecha = "FechaConsulta",
        TipoFecha    = TipoFechaSello.Date,
        Notas        = "Afiliados MutualSer: dos celulares, fijo, email, régimen y localización.",
        ColumnasDato =
        [
            new("NumeroTelefonoCelular", ["numerotelefonocelular", "celular", "celular1", "numerocelular", "telefonocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("NumeroTelefonoCelular2", ["numerotelefonocelular2", "celular2", "numerocelular2", "telefonocelular2", "movil2"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Segundo celular"),
            new("NumeroTelefonoFijo", ["numerotelefonofijo", "fijo", "telefonofijo", "numerofijo"],
                TipoColumnaCargue.Fijo, 10, Descripcion: "Fijo (10 dígitos, empieza en 60)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
            new("Regimen", ["regimen", "tiporegimen"],
                TipoColumnaCargue.Texto, 30, EntraAlFlag: false, Descripcion: "Contributivo, subsidiado…"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario…"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Departamento"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio"),
        ],
    };

    /// <summary>
    /// Nueva EPS. Sin regla propia (<c>ReglaGenerica</c>): el SP legacy no limpiaba nada que el
    /// motor no haga ya. Verificado contra <c>sys.columns</c> de <c>dbo.NuevaEps</c> el 2026-09-23:
    /// <b>TitularID es varchar(30)</b> (las demás fuentes son bigint; índice
    /// IX_NuevaEps_TitularID_FechaConsulta; sin FK ni triggers) → <see cref="DefinicionFuenteCargue.TitularIdComoTexto"/>:
    /// el documento se valida como número y se guarda <c>CONVERT(varchar(30), bigint)</c>, sin
    /// ceros a la izquierda. NumeroTelefono es celular: la BaseMadre lo expone como [Celular NuevaEps].
    /// El flag, como en el SP, solo mira teléfono, estado y tipo de afiliación.
    /// </summary>
    public static readonly DefinicionFuenteCargue NuevaEps = new()
    {
        Fuente             = "NuevaEps",
        Etiqueta           = "Nueva EPS",
        TablaDestino       = "dbo.NuevaEps",
        ColumnaFlag        = "EstadoEnNuevaEps",
        ColumnaFecha       = "FechaConsulta",
        TipoFecha          = TipoFechaSello.Date,
        TitularIdComoTexto = true,
        Notas              = "Afiliados Nueva EPS: celular, afiliación y localización.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("EstadoAfiliacion", ["estadoafiliacion", "estadodeafiliacion"],
                TipoColumnaCargue.Texto, 30, Descripcion: "Activo, retirado…"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, Descripcion: "Cotizante, beneficiario…"),
            new("FechaNacimiento", ["fechanacimiento", "fechadenacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de nacimiento"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Departamento"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio"),
        ],
    };

    /// <summary>
    /// SIMIT (placas de vehículos). El archivo trae una fila por titular con Placa1..Placa15
    /// (como SIMITAuxiliar: 15 columnas, la 15 era nchar(10)); el destino guarda una fila por
    /// placa. <c>ReglaSimit.Expandir</c> hace el despivote. Verificado contra <c>sys.columns</c>
    /// de <c>dbo.SIMIT</c> el 2026-09-23 (Placa varchar(10) NULL, FechaConsulta date, FK a
    /// Titular, sin triggers).
    /// </summary>
    public static readonly DefinicionFuenteCargue Simit = new()
    {
        Fuente       = "SIMIT",
        Etiqueta     = "SIMIT (placas)",
        TablaDestino = "dbo.SIMIT",
        ColumnaFlag  = "EstadoEnSIMIT",
        ColumnaFecha = "FechaConsulta",
        TipoFecha    = TipoFechaSello.Date,
        Cardinalidad = CardinalidadCargue.VariasPorTitular,
        Notas        = "Placas de vehículos: Placa1..Placa15 por titular, una fila por placa en SIMIT.",
        ColumnasDato =
        [
            new("Placa", [], TipoColumnaCargue.Placa, 10, DesdeArchivo: false,
                Descripcion: "Una fila por placa distinta de Placa1..Placa15"),
        ],
        ColumnasArchivoExtra = Enumerable.Range(1, 15)
            .Select(i => new ColumnaFuenteCargue($"Placa{i}", i == 1 ? ["placa1", "placa"] : [$"placa{i}"],
                TipoColumnaCargue.Placa, 10, Descripcion: i == 1 ? "Placa (o Placa1)" : $"Placa {i}"))
            .ToList(),
    };

    /// <summary>
    /// Datos Claro (operador). N:1: un titular puede tener varios teléfonos/correos; no crea
    /// Titular ni relaciones (la tabla no tiene FK a Titular), no tiene flag ni política de
    /// vacías, y el sello es <c>FechaInsercion</c>. Regla propia: lista negra de teléfonos y
    /// exclusión de filas sin teléfono ni email. Verificado contra <c>sys.columns</c> de
    /// <c>dbo.DatosClaro</c> el 2026-09-23 (FechaInsercion <c>date</c> NULL, sin FK ni triggers).
    /// NumeroTelefono acepta celular o fijo: el SP no filtraba por tipo.
    /// </summary>
    public static readonly DefinicionFuenteCargue DatosClaro = new()
    {
        Fuente               = "DatosClaro",
        Etiqueta             = "Datos Claro",
        TablaDestino         = "dbo.DatosClaro",
        ColumnaFlag          = null,
        ColumnaFecha         = "FechaInsercion",
        TipoFecha            = TipoFechaSello.Date,
        CreaTitular          = false,
        CreaTitularCartera   = false,
        Cardinalidad         = CardinalidadCargue.VariasPorTitular,
        AplicaPoliticaVacias = false,
        Notas                = "Teléfonos y correos del operador Claro (varios por titular). No crea titulares.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil", "linea", "numerolinea"],
                TipoColumnaCargue.Telefono, 10, Descripcion: "Teléfono (celular 3xx o fijo 60x, 10 dígitos)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 100, Descripcion: "Correo electrónico"),
        ],
    };

    // ── Fase 2, grupo salud ──────────────────────────────────────────────────

    /// <summary>
    /// Positiva (ARL). Regla propia: un fijo que llega en la columna de celular pasa al fijo
    /// (sin concatenar). Verificado contra <c>sys.columns</c> de <c>dbo.Positiva</c> el
    /// 2026-09-24: NumeroTelefonoFijo es <b>varchar(30)</b> (el SP concatenaba "fijo, celular"
    /// ahí; 522 filas legacy lo tienen; el motor guarda un solo fijo de 10 dígitos),
    /// FechaNacimiento <c>date</c> NULL, FechaConsulta <c>date</c> NOT NULL, FK a Titular, sin
    /// triggers; 2.213.891 filas / 1.967.407 titulares. El flag, como en el SP, solo mira celular,
    /// fijo y email (FechaNacimiento, Departamento y Municipio no).
    /// </summary>
    public static readonly DefinicionFuenteCargue Positiva = new()
    {
        Fuente          = "Positiva",
        Etiqueta        = "Positiva (ARL)",
        TablaDestino    = "dbo.Positiva",
        ColumnaFlag     = "EstadoEnPositiva",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliados ARL Positiva: celular, fijo, email y localización.",
        ColumnasDato =
        [
            new("NumeroTelefonoCelular", ["numerotelefonocelular", "celular", "numerocelular", "telefonocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("NumeroTelefonoFijo", ["numerotelefonofijo", "fijo", "telefonofijo", "numerofijo"],
                TipoColumnaCargue.Fijo, 30, Descripcion: "Fijo (10 dígitos, empieza en 6)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 100, Descripcion: "Correo electrónico"),
            new("FechaNacimiento", ["fechanacimiento", "fechadenacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de nacimiento"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Departamento"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio"),
        ],
    };

    /// <summary>
    /// Salud Total (EPS). Sin regla propia (<c>ReglaGenerica</c>): <c>sp_ProcesarSaludTotalAuxiliar</c>
    /// solo pasaba vacíos a NULL (incluidos NIT 0 y la fecha 1900-01-01), creaba Titular y
    /// relaciones, calculaba el flag e insertaba: todo genérico. Verificado contra
    /// <c>sys.columns</c> de <c>dbo.SaludTotal</c> el 2026-09-24 (FechaNacimiento y
    /// UltimaFechaPago <c>date</c> NULL, FechaConsulta <c>date</c> NOT NULL, NIT bigint,
    /// IngresoBase varchar(20); FK a Titular, sin triggers; 1.586.180 filas / 1.525.448 titulares).
    /// NumeroTelefono es celular: el 100 % de las filas con dato es 3xx de 10 dígitos y la BaseMadre
    /// lo expone como [Celular Salud Total]. El flag, como en el SP, mira teléfono, email y NIT.
    /// </summary>
    public static readonly DefinicionFuenteCargue SaludTotal = new()
    {
        Fuente          = "SaludTotal",
        Etiqueta        = "Salud Total (EPS)",
        TablaDestino    = "dbo.SaludTotal",
        ColumnaFlag     = "EstadoEnSaludTotal",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliados EPS Salud Total: celular, email, afiliación y empleador.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
            new("FechaNacimiento", ["fechanacimiento", "fechadenacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de nacimiento"),
            new("EstadoAfiliacion", ["estadoafiliacion", "estadodeafiliacion"],
                TipoColumnaCargue.Texto, 30, EntraAlFlag: false, Descripcion: "Activo, retirado…"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario…"),
            new("Ciudad", ["ciudad", "municipio"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Ciudad"),
            new("NIT", ["nit", "nitempresa", "nitempleador"],
                TipoColumnaCargue.Entero, EsNit: true, Descripcion: "NIT de la empresa empleadora ('NIT-DV' se acepta: se quita el DV)"),
            new("Empresa", ["empresa", "nombreempresa", "empleador"],
                TipoColumnaCargue.Texto, 100, EntraAlFlag: false, Descripcion: "Empresa empleadora"),
            new("Cargo", ["cargo"],
                TipoColumnaCargue.Texto, 60, EntraAlFlag: false, Descripcion: "Cargo en la empresa"),
            new("UltimaFechaPago", ["ultimafechapago", "fechaultimopago", "fechaultimapago", "ultimopago"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Último pago de aportes"),
            new("IngresoBase", ["ingresobase", "ibc", "ingresobasecotizacion", "salario"],
                TipoColumnaCargue.Texto, 20, EntraAlFlag: false, Descripcion: "Ingreso base de cotización"),
        ],
    };

    /// <summary>
    /// SOS (EPS Servicio Occidental de Salud): empleador, teléfono y dirección. Sin regla propia
    /// (<c>ReglaGenerica</c>): <c>sp_ProcesarSOSAuxiliar</c> solo pasaba vacíos a NULL (NitEmpresa 0
    /// incluido), creaba Titular y relaciones, calculaba el flag e insertaba. Verificado contra
    /// <c>sys.columns</c> de <c>dbo.SOS</c> el 2026-09-24 (NitEmpresa bigint, RazonSocial
    /// varchar(100), Telefono varchar(10), <b>Direccion varchar(70)</b> —en SOSAuxiliar era
    /// <c>Dirección varchar(150)</c>—, FechaConsulta <c>date</c> NOT NULL; FK a Titular, sin
    /// triggers; 226.946 filas / 226.878 titulares). El flag, como en el SP, mira las cuatro columnas.
    /// Telefono se trata como <b>celular</b> (mismo criterio que NuevaEps, ADR-039 §4): la BaseMadre
    /// lo expone como [Celular SOS] y alimenta el selector de celulares del Creador. De las 2.099
    /// filas con dato, 1.372 (65 %) son 3xx de 10 dígitos; 625 son fijos de 7 dígitos sin indicativo
    /// (SOS no trae departamento ni municipio para resolverlo) y 34 son fijos 60x: esos se anulan
    /// con advertencia.
    /// </summary>
    public static readonly DefinicionFuenteCargue Sos = new()
    {
        Fuente          = "SOS",
        Etiqueta        = "SOS (EPS)",
        TablaDestino    = "dbo.SOS",
        ColumnaFlag     = "EstadoEnSOS",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliados EPS SOS: empleador (NIT y razón social), celular y dirección.",
        ColumnasDato =
        [
            new("NitEmpresa", ["nitempresa", "nit", "nitempleador"],
                TipoColumnaCargue.Entero, EsNit: true, Descripcion: "NIT de la empresa empleadora ('NIT-DV' se acepta: se quita el DV)"),
            new("RazonSocial", ["razonsocial", "empresa", "nombreempresa", "empleador"],
                TipoColumnaCargue.Texto, 100, Descripcion: "Razón social del empleador"),
            new("Telefono", ["telefono", "celular", "numerotelefono", "numerocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Direccion", ["direccion", "direcciondomicilio", "domicilio"],
                TipoColumnaCargue.Texto, 70, Descripcion: "Dirección"),
        ],
    };

    /// <summary>
    /// Coosalud (EPS). Sin regla propia (<c>ReglaGenerica</c>): <c>sp_ProcesarCoosaludAuxiliar</c>
    /// anulaba teléfonos de largo distinto de 10 y correos de la lista negra, calculaba el flag e
    /// insertaba solo lo nuevo: todo genérico (y más estricto en el motor). Verificado contra
    /// <c>sys.columns</c> de <c>dbo.Coosalud</c> el 2026-09-24 (todas las de texto varchar(50) salvo
    /// Email 255, FechaNacimiento <c>date</c> NULL, FechaConsulta <c>date</c> NOT NULL; <b>sin FK a
    /// Titular</b> pero el SP sí creaba Titular y Titular_Cartera, y el motor también; sin triggers;
    /// 1.037.887 filas / 1.032.725 titulares). NumeroTelefono es celular (77.977 de las 77.983 filas
    /// con dato son 3xx y la BaseMadre lo expone como [Celular Coosalud]). El flag, como en el SP,
    /// mira teléfono y email.
    /// </summary>
    public static readonly DefinicionFuenteCargue Coosalud = new()
    {
        Fuente          = "Coosalud",
        Etiqueta        = "Coosalud (EPS)",
        TablaDestino    = "dbo.Coosalud",
        ColumnaFlag     = "EstadoEnCoosalud",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliados EPS Coosalud: celular, email, afiliación y localización de origen.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
            new("MunicipioOrigen", ["municipioorigen", "municipiodeorigen", "municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio de origen"),
            new("DepartamentoOrigen", ["departamentoorigen", "departamentodeorigen", "departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Departamento de origen"),
            new("Regimen", ["regimen", "tiporegimen"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Contributivo, subsidiado…"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario…"),
            new("EstadoAfiliacion", ["estadoafiliacion", "estadodeafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Activo, retirado…"),
            new("NivelSisben", ["nivelsisben", "sisben", "niveldesisben"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Nivel del Sisbén"),
            new("FechaNacimiento", ["fechanacimiento", "fechadenacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de nacimiento"),
        ],
    };

    /// <summary>
    /// Savia Salud (EPS). Regla propia: celdas con varios números separados por coma se reparten
    /// entre NumeroCelular y NumeroTelefono sin concatenar. Verificado contra <c>sys.columns</c> de
    /// <c>dbo.SaviaSalud</c> el 2026-09-24: el flag se llama <b><c>Estado</c></b> (bit NULL), TitularID
    /// y FechaConsulta son NULL-ables, FechaDeNacimiento <c>date</c>, <b>NumeroCelular varchar(100)</b>
    /// (el SP guardaba ahí listas '3147630194,3148795401'), NumeroTelefono varchar(10); <b>no hay
    /// columna de correo</b> aunque SaviaSaludAuxiliar sí la tenía; sin FK a Titular (el SP igual
    /// creaba Titular y relaciones), sin triggers; 719.743 filas / 717.531 titulares. El flag mira
    /// los dos teléfonos (el SP contaba también el correo, que no guarda).
    /// </summary>
    public static readonly DefinicionFuenteCargue SaviaSalud = new()
    {
        Fuente          = "SaviaSalud",
        Etiqueta        = "Savia Salud (EPS)",
        TablaDestino    = "dbo.SaviaSalud",
        ColumnaFlag     = "Estado",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliados EPS Savia Salud: celular, teléfono y afiliación. No guarda correo.",
        ColumnasDato =
        [
            new("FechaDeNacimiento", ["fechadenacimiento", "fechanacimiento", "nacimiento"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de nacimiento"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario…"),
            new("EstadoDeAfiliacion", ["estadodeafiliacion", "estadoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Activo, retirado…"),
            new("Regimen", ["regimen", "tiporegimen"],
                TipoColumnaCargue.Texto, 70, EntraAlFlag: false, Descripcion: "Contributivo, subsidiado…"),
            new("NumeroTelefono", ["numerotelefono", "telefono", "telefonofijo", "fijo"],
                TipoColumnaCargue.Telefono, 10, Descripcion: "Teléfono (celular 3xx o fijo 60x, 10 dígitos)"),
            new("NumeroCelular", ["numerocelular", "celular", "telefonocelular", "movil"],
                TipoColumnaCargue.Celular, 100, Descripcion: "Celular (10 dígitos, empieza en 3; si trae varios, se reparten)"),
        ],
    };

    /// <summary>
    /// MiVacuna (registro de vacunación). Sin regla propia (<c>ReglaGenerica</c>):
    /// <c>sp_ProcesarMiVacunaAuxiliar</c> solo pasaba el celular '' o '0' a NULL, cruzaba la lista
    /// negra de correos, calculaba el flag e insertaba: todo genérico. Verificado contra
    /// <c>sys.columns</c> de <c>dbo.MiVacuna</c> el 2026-09-24 (FechaExpedicion <c>date</c>, Celular
    /// varchar(10), Email varchar(255), EstadoEnMiVacuna y FechaConsulta NULL-ables; sin FK a
    /// Titular —el SP igual creaba Titular y relaciones—, sin triggers; 36.985 filas, una por
    /// titular). El flag, como en el SP, mira celular y email.
    /// </summary>
    public static readonly DefinicionFuenteCargue MiVacuna = new()
    {
        Fuente          = "MiVacuna",
        Etiqueta        = "MiVacuna",
        TablaDestino    = "dbo.MiVacuna",
        ColumnaFlag     = "EstadoEnMiVacuna",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Registro MiVacuna: celular, email y fecha de expedición del documento.",
        ColumnasDato =
        [
            new("FechaExpedicion", ["fechaexpedicion", "fechadeexpedicion", "expedicion"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de expedición del documento"),
            new("Celular", ["celular", "numerocelular", "telefono", "numerotelefono", "movil"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
        ],
    };

    // ── Fase 2 · grupo "otras" ───────────────────────────────────────────────

    /// <summary>
    /// Fosyga (ADRES / BDUA: afiliación a salud). Regla propia (<c>ReglaFosyga</c>): Estado
    /// 'AFILIADO FALLECIDO' → TipoAfiliado 'FALLECIDO', como el SP. Verificado contra
    /// <c>sys.columns</c> de <c>dbo.Fosyga</c> el 2026-09-24: EPS varchar(120) (en FosygaAuxiliar
    /// era varchar(200): un nombre de EPS largo tumbaba el INSERT entero del SP), Departamento
    /// varchar(70), fechas y FechaConsulta <c>date</c>; FK a Titular, sin triggers; heap con
    /// IX_Fosyga_TitularID_Fecha. El flag, como en el SP, solo mira Estado, EPS y Régimen.
    /// </summary>
    public static readonly DefinicionFuenteCargue Fosyga = new()
    {
        Fuente          = "Fosyga",
        Etiqueta        = "Fosyga (ADRES)",
        TablaDestino    = "dbo.Fosyga",
        ColumnaFlag     = "EstadoEnFosyga",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Afiliación a salud según ADRES: estado, EPS, régimen, fechas, tipo de afiliado y localización.",
        ColumnasDato =
        [
            new("Estado", ["estado", "estadoafiliacion", "estadodeafiliacion"],
                TipoColumnaCargue.Texto, 30, Descripcion: "Activo, retirado, afiliado fallecido…"),
            new("EPS", ["eps", "entidad", "nombreentidad", "nombreeps", "entidadpromotora"],
                TipoColumnaCargue.Texto, 120, Descripcion: "EPS (entidad) a la que está afiliado"),
            new("Regimen", ["regimen", "tiporegimen"],
                TipoColumnaCargue.Texto, 30, Descripcion: "Contributivo, subsidiado…"),
            new("FechaAfiliacionEfectiva", ["fechaafiliacionefectiva", "fechadeafiliacionefectiva", "fechaafiliacion"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de afiliación efectiva"),
            new("FechaFinalizacionAfiliacion", ["fechafinalizacionafiliacion", "fechadefinalizaciondeafiliacion", "fechafinalizacion", "fechafinafiliacion"],
                TipoColumnaCargue.Fecha, EntraAlFlag: false, Descripcion: "Fecha de finalización (31/12/2999 = vigente)"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario, cabeza de familia…"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 70, EntraAlFlag: false, Descripcion: "Departamento"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio"),
        ],
    };

    /// <summary>
    /// RGM (vehículos: marca, modelo y placa). Regla propia (<c>ReglaRgm</c>): el modelo debe ser un
    /// año plausible. Verificado contra <c>sys.columns</c> de <c>dbo.RGM</c> el 2026-09-24: Marca
    /// varchar(50), <b>Modelo int</b> (en RGMAuxiliar era varchar(50): un modelo no numérico tumbaba
    /// el INSERT del SP), Placa varchar(50) (en RGMAuxiliar varchar(6), que truncaba), FechaConsulta
    /// <c>date</c>; FK a Titular (FK_RGM), sin triggers; heap con IX_RGM_TitularID_Fecha. Una fila
    /// vigente por titular (el SP deduplicaba el staging por titular; 1.663.543 filas, 1.662.514
    /// titulares). La placa usa la misma validación que SIMIT (<c>PlacaVehiculo</c>): en dbo.RGM el
    /// 99,7 % de las 8.765 placas la cumple; las 25 que no son '[objec' ('[object Object]' truncado
    /// a 6), 'AVAL' y '0'. El flag, como en el SP, es marca, modelo o placa con valor.
    /// </summary>
    public static readonly DefinicionFuenteCargue Rgm = new()
    {
        Fuente          = "RGM",
        Etiqueta        = "RGM (vehículos)",
        TablaDestino    = "dbo.RGM",
        ColumnaFlag     = "EstadoEnRGM",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Vehículo por titular: marca, modelo (año) y placa.",
        ColumnasDato =
        [
            new("Marca", ["marca", "marcavehiculo"],
                TipoColumnaCargue.Texto, 50, Descripcion: "Marca del vehículo"),
            new("Modelo", ["modelo", "modelovehiculo", "anomodelo"],
                TipoColumnaCargue.Entero, EnteroSql: TipoEnteroSql.Int, Descripcion: "Año del modelo (se anula si no es un año plausible)"),
            new("Placa", ["placa", "placavehiculo", "numeroplaca"],
                TipoColumnaCargue.Placa, 50, Descripcion: "Placa (sin espacios ni guiones; lo que no es placa se anula)"),
        ],
    };

    /// <summary>
    /// Tigo Une (operador). Sin regla propia (<c>ReglaGenerica</c>): <c>sp_ProcesarTigoUneAuxiliar</c>
    /// solo hacía lo genérico (lista negra de correos —con LIKE—, teléfono de 10 caracteres, flag).
    /// Es una fuente DISTINTA de Tigo (<c>dbo.Tigo</c>): tabla, SP, columnas de la BaseMadre
    /// ([Celular TigoUne]/[Email TigoUne]) y fila del Calificador propias; comparten solo la forma.
    /// Verificado contra <c>sys.columns</c> de <c>dbo.TigoUne</c> el 2026-09-24: NumeroTelefono
    /// varchar(10), Email varchar(255), FechaConsulta <c>date</c>; <b>sin FK a Titular</b> (el SP
    /// igual creaba el Titular y la relación, y el motor también), sin triggers ni CHECK; heap con
    /// IX_TigoUne_TitularID_FechaConsulta. NumeroTelefono es celular: la BaseMadre lo expone como
    /// [Celular TigoUne] (42.581 de sus 42.585 números empiezan en 3). Una fila vigente por titular
    /// (el SP deduplicaba el staging por titular).
    /// </summary>
    public static readonly DefinicionFuenteCargue TigoUne = new()
    {
        Fuente          = "TigoUne",
        Etiqueta        = "Tigo Une (operador)",
        TablaDestino    = "dbo.TigoUne",
        ColumnaFlag     = "EstadoEnTigoUne",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Celular y correo del operador Tigo Une.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil", "linea", "numerolinea"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
        ],
    };

    /// <summary>
    /// Tigo (operador). <b>No tiene SP que traducir</b>: <c>dbo.Tigo</c> se llenó a mano en
    /// febrero-marzo de 2025 desde <c>TigoAuxiliar</c> (sin Nombre, sin Cartera/Líder: los titulares
    /// ya existían) y no recibe nada desde el 2025-03-14; ningún objeto de la BD, trabajo del Agente
    /// ni paquete SSIS escribe en ella. Sigue EN USO: la leen las cuatro BaseMadre ([Celular Tigo] /
    /// [Email Tigo]), el Calificador, Rastreo, Uso de Base de Datos, Consulta Integrada y
    /// sp_Actualizar_AuditoriaMensajeria; este cargue es la forma de actualizarla. Definición por
    /// analogía con su gemela <c>TigoUne</c> (misma forma, <c>ReglaGenerica</c>). Verificado contra
    /// <c>sys.columns</c> de <c>dbo.Tigo</c> el 2026-09-24: NumeroTelefono varchar(10), Email
    /// varchar(255), FechaConsulta <c>date</c> (defaults EstadoEnTigo 0 y FechaConsulta getdate(),
    /// que el motor no usa: escribe los dos); FK a Titular (por eso crea Titular y el archivo debe
    /// traer Nombre, como toda fuente con FK); CHECK chk_Telefono (7 o 10 dígitos que empiezan en 3
    /// o 6: un celular validado por el motor siempre lo cumple); sin triggers; heap con
    /// IX_Tigo_TitularID_Fecha. NumeroTelefono es celular ([Celular Tigo]; 340.972 de sus 342.128
    /// números empiezan en 3; los 1.101 fijos y 55 de 7 dígitos legacy no se tocan).
    /// </summary>
    public static readonly DefinicionFuenteCargue Tigo = new()
    {
        Fuente          = "Tigo",
        Etiqueta        = "Tigo (operador)",
        TablaDestino    = "dbo.Tigo",
        ColumnaFlag     = "EstadoEnTigo",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Celular y correo del operador Tigo. Sin SP legacy: definición por analogía con Tigo Une.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil", "linea", "numerolinea"],
                TipoColumnaCargue.Celular, 10, Descripcion: "Celular (10 dígitos, empieza en 3)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
        ],
    };

    // ── Fase 2 — grupo empresas ─────────────────────────────────────────────

    /// <summary>
    /// RUES (registro mercantil del titular). Sin regla propia (<c>ReglaGenerica</c>):
    /// <c>sp_ProcesarRUESAuxiliar</c> solo creaba Titular/relaciones, calculaba el flag e
    /// insertaba. Verificado contra <c>sys.columns</c> de <c>dbo.RUES</c> el 2026-09-24:
    /// <b>FechaConsulta es <c>datetime</c></b> (la única de las fuentes con sello con hora: el SP
    /// guardaba <c>GETDATE()</c> y 799.156 de sus 873.188 filas tienen hora) → sello
    /// <see cref="TipoFechaSello.DateTime"/>; FK a Titular, índice (TitularID, FechaConsulta DESC),
    /// sin triggers. El flag, como en el SP, mira matrícula, estado y actividad; NombreEmpresa no
    /// (el SP lo dejaba fuera y en la tabla no hay ninguna fila con solo NombreEmpresa).
    /// </summary>
    public static readonly DefinicionFuenteCargue Rues = new()
    {
        Fuente          = "RUES",
        Etiqueta        = "RUES (registro mercantil)",
        TablaDestino    = "dbo.RUES",
        ColumnaFlag     = "EstadoEnRUES",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.DateTime,
        FaseIntegracion = 2,
        Notas           = "Matrícula mercantil del titular: número, estado, actividad económica y nombre de la empresa.",
        ColumnasDato =
        [
            new("NumeroMatricula", ["numeromatricula", "matricula", "numerodematricula", "nomatricula", "matriculamercantil"],
                TipoColumnaCargue.Entero, Descripcion: "Número de matrícula mercantil"),
            new("EstadoEmpresa", ["estadoempresa", "estadomatricula", "estadodelamatricula", "estado"],
                TipoColumnaCargue.Texto, 20, Descripcion: "Activa, cancelada, inactiva…"),
            new("ActividadEconomica", ["actividadeconomica", "actividad", "descripcionactividad"],
                TipoColumnaCargue.Texto, 250, Descripcion: "Actividad económica registrada"),
            new("NombreEmpresa", ["nombreempresa", "empresa", "nombredelaempresa", "nombreestablecimiento"],
                TipoColumnaCargue.Texto, 70, EntraAlFlag: false, Descripcion: "Nombre de la empresa o establecimiento"),
        ],
    };

    /// <summary>
    /// Cámara de Comercio (datos de contacto del titular como comerciante). Regla propia
    /// (<c>ReglaCamara</c>): indicativo a los fijos de 7 dígitos y textos de relleno del formulario
    /// en Departamento. Verificado contra <c>sys.columns</c> de <c>dbo.Camara</c> el 2026-09-24:
    /// NumeroTelefono varchar(10) (mezcla celulares 3xx y fijos 60x → tipo <c>Telefono</c>),
    /// Email varchar(255), Municipio varchar(50), <b>Departamento varchar(70)</b>, FechaConsulta
    /// <c>date</c>; FK a Titular, índice (TitularID, FechaConsulta DESC), sin triggers. El flag,
    /// como en el SP, solo mira teléfono y email.
    /// </summary>
    public static readonly DefinicionFuenteCargue Camara = new()
    {
        Fuente          = "Camara",
        Etiqueta        = "Cámara de Comercio (personas)",
        TablaDestino    = "dbo.Camara",
        ColumnaFlag     = "EstadoEnCamara",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Teléfono (celular o fijo; el fijo de 7 dígitos recibe indicativo), email y localización del comerciante.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil", "telefonofijo", "fijo"],
                TipoColumnaCargue.Telefono, 10, Descripcion: "Teléfono (celular 3xx o fijo 60x; fijo de 7 dígitos → indicativo)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
            new("Municipio", ["municipio", "ciudad"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Municipio (resuelve el indicativo del fijo)"),
            new("Departamento", ["departamento", "depto", "dpto"],
                TipoColumnaCargue.Texto, 70, EntraAlFlag: false, Descripcion: "Departamento (resuelve el indicativo del fijo)"),
        ],
    };

    /// <summary>
    /// Cámara de Comercio (empresas): teléfono y email de la EMPRESA, identificada por su NIT.
    /// Primera fuente cuya clave no es TitularID (<see cref="DefinicionFuenteCargue.ColumnaClaveTitular"/>
    /// = NitEmpresa): no crea Titular ni relaciones (su SP tampoco) y se cruza con ARLSura por NIT
    /// (<c>vw_DatosCamaraEmpresa</c>: <c>ARLSura.NIT = CamaraEmpresa.NitEmpresa</c>). Regla propia
    /// (<c>ReglaCamaraEmpresa</c>): quita el dígito de verificación de 'NIT-DV' e indicativo a los
    /// fijos de 7 dígitos por el Departamento del archivo, que no se guarda. Verificado contra
    /// <c>sys.columns</c> de <c>dbo.CamaraEmpresa</c> el 2026-09-24: NitEmpresa bigint NULL,
    /// NumeroTelefono varchar(10), Email varchar(255), EstadoEnCamara bit, FechaConsulta date;
    /// heap sin índices, sin FK ni triggers. El flag, como en el SP, mira teléfono y email.
    /// </summary>
    public static readonly DefinicionFuenteCargue CamaraEmpresa = new()
    {
        Fuente                  = "CamaraEmpresa",
        Etiqueta                = "Cámara de Comercio (empresas)",
        TablaDestino            = "dbo.CamaraEmpresa",
        ColumnaFlag             = "EstadoEnCamara",
        ColumnaFecha            = "FechaConsulta",
        TipoFecha               = TipoFechaSello.Date,
        ColumnaClaveTitular     = "NitEmpresa",
        AliasClaveTitular       = ["nitempresa", "nit", "nitdelaempresa", "numeronit"],
        DescripcionClaveTitular = "NIT de la empresa sin dígito de verificación (5 a 15 dígitos; 'NIT-DV' se acepta y se le quita el DV)",
        CreaTitular             = false,
        CreaTitularCartera      = false,
        FaseIntegracion         = 2,
        Notas                   = "Teléfono y email de empresas por NIT (no crea titulares). El fijo de 7 dígitos recibe indicativo por el departamento.",
        ColumnasDato =
        [
            new("NumeroTelefono", ["numerotelefono", "telefono", "celular", "numerocelular", "movil", "telefonofijo", "fijo"],
                TipoColumnaCargue.Telefono, 10, Descripcion: "Teléfono (celular 3xx o fijo 60x; fijo de 7 dígitos → indicativo)"),
            new("Email", ["email", "correo", "correoelectronico", "mail"],
                TipoColumnaCargue.Email, 255, Descripcion: "Correo electrónico"),
        ],
        // No se guardan: solo resuelven el indicativo del fijo de 7 dígitos (CamaraEmpresaAuxiliar
        // tenía Departamento varchar(60); Municipio no estaba y aquí es opcional).
        ColumnasArchivoExtra =
        [
            new("Departamento", ["departamento", "depto", "dpto"], TipoColumnaCargue.Texto, 60,
                Descripcion: "Departamento (solo resuelve el indicativo del fijo; no se guarda)"),
            new("Municipio", ["municipio", "ciudad"], TipoColumnaCargue.Texto, 50,
                Descripcion: "Municipio (opcional; solo resuelve el indicativo del fijo; no se guarda)"),
        ],
    };

    /// <summary>
    /// Positiva ARL, empleador del titular (NIT y razón social). Sin regla propia
    /// (<c>ReglaGenerica</c>): <c>sp_ProcesarPositivaEmpresasAuxiliar</c> solo creaba Titular y
    /// relaciones, calculaba el flag e insertaba. Verificado contra <c>sys.columns</c> de
    /// <c>dbo.PositivaEmpresas</c> el 2026-09-24: NIT bigint, Empresa varchar(230),
    /// EstadoAfiliacion varchar(30), EstadoEnPositivaEmpresa bit, FechaConsulta date; FK a Titular,
    /// índice (TitularID, FechaConsulta), sin triggers. El flag, como en el SP, mira NIT y Empresa.
    /// Las razones sociales llegan partidas en varias líneas (texto de un PDF): el motor colapsa
    /// los saltos de línea a un espacio.
    /// </summary>
    public static readonly DefinicionFuenteCargue PositivaEmpresas = new()
    {
        Fuente          = "PositivaEmpresas",
        Etiqueta        = "Positiva ARL (empleador)",
        TablaDestino    = "dbo.PositivaEmpresas",
        ColumnaFlag     = "EstadoEnPositivaEmpresa",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Empleador del titular según Positiva ARL: NIT, razón social y estado de la afiliación.",
        ColumnasDato =
        [
            new("NIT", ["nit", "nitempresa", "nitempleador"],
                TipoColumnaCargue.Entero, EsNit: true, Descripcion: "NIT de la empresa empleadora ('NIT-DV' se acepta: se quita el DV)"),
            new("Empresa", ["empresa", "nombreempresa", "empleador", "razonsocialempresa"],
                TipoColumnaCargue.Texto, 230, Descripcion: "Empresa empleadora"),
            new("EstadoAfiliacion", ["estadoafiliacion", "estadodeafiliacion", "estado"],
                TipoColumnaCargue.Texto, 30, EntraAlFlag: false, Descripcion: "Activa, inactiva, retirado…"),
        ],
    };

    /// <summary>
    /// ARL Sura: empleador del titular (NIT y empresa). Fuente DISTINTA de <see cref="Sura"/> (la
    /// EPS): otra tabla (<c>dbo.ARLSura</c>), otra clave de ruta ("ARLSura" ≠ "Sura", el catálogo
    /// compara por igualdad, nunca por prefijo) y otra etiqueta; en la BaseMadre sus alias son
    /// <c>[NIT ARL Sura]</c>/<c>[Empresa ARL Sura]</c> desde la integración de Sura (2026-09-02). Su
    /// flag se llama igual que el de la EPS (<c>EstadoEnSura</c>), pero es otra columna de otra tabla.
    /// Sin regla propia (<c>ReglaGenerica</c>): <c>sp_ProcesarArlSuraAuxiliar</c> solo creaba Titular
    /// y relaciones, calculaba el flag e insertaba. Verificado contra <c>sys.columns</c> de
    /// <c>dbo.ARLSura</c> el 2026-09-24: NIT bigint, Empresa varchar(100), EstadoEnSura bit,
    /// FechaConsulta date; FK a Titular, índice (TitularID, FechaConsulta DESC), sin triggers;
    /// 1.565.455 filas. El flag, como en el SP, mira NIT y Empresa.
    /// </summary>
    public static readonly DefinicionFuenteCargue ArlSura = new()
    {
        Fuente          = "ARLSura",
        Etiqueta        = "ARL Sura (empleador)",
        TablaDestino    = "dbo.ARLSura",
        ColumnaFlag     = "EstadoEnSura",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "Empleador del titular según ARL Sura (no es la EPS Sura): NIT y empresa.",
        ColumnasDato =
        [
            new("NIT", ["nit", "nitempresa", "nitempleador"],
                TipoColumnaCargue.Entero, EsNit: true, Descripcion: "NIT de la empresa empleadora ('NIT-DV' se acepta: se quita el DV)"),
            new("Empresa", ["empresa", "nombreempresa", "empleador", "razonsocialempresa"],
                TipoColumnaCargue.Texto, 100, Descripcion: "Empresa empleadora"),
        ],
    };

    /// <summary>
    /// Compensados (BDUA: EPS y último período compensado del titular). Regla propia
    /// (<c>ReglaCompensados</c>): la marca del proveedor 'NO SE ENCUENTRA' no es un dato.
    /// Verificado contra <c>sys.columns</c> de <c>dbo.Compensados</c> el 2026-09-24: EPS varchar(50),
    /// UltimoPeriodoCompensado <c>date</c>, TipoAfiliado varchar(50), <b>EstadoEnCompensados int</b>
    /// NOT NULL (no bit: el motor escribe 0/1), FechaConsulta date; FK a Titular, índice
    /// (TitularID, FechaConsulta DESC), sin triggers.
    ///
    /// <b>El flag NO viene del archivo</b> (no hizo falta la extensión <c>FlagDesdeArchivo</c> prevista
    /// en el plan): <c>sp_ProcesarCompensadosAuxiliar</c> recalculaba <c>Estado</c> como
    /// "Estado, EPS o UltimoPeriodoCompensado no nulos", así que la columna Estado del archivo solo
    /// servía para dar flag 1 a una fila sin ningún dato (2.315 filas así hoy). Aquí el flag es EPS o
    /// período, lo que se guarda; la columna Estado del archivo se informa como "no se usa".
    /// TipoAfiliado no entra al flag, como en el SP.
    /// </summary>
    public static readonly DefinicionFuenteCargue Compensados = new()
    {
        Fuente          = "Compensados",
        Etiqueta        = "Compensados (BDUA)",
        TablaDestino    = "dbo.Compensados",
        ColumnaFlag     = "EstadoEnCompensados",
        ColumnaFecha    = "FechaConsulta",
        TipoFecha       = TipoFechaSello.Date,
        FaseIntegracion = 2,
        Notas           = "EPS, último período compensado y tipo de afiliado del titular. El Estado del archivo no se usa: el flag sale de EPS y período.",
        ColumnasDato =
        [
            new("EPS", ["eps", "nombreeps", "entidad", "administradora"],
                TipoColumnaCargue.Texto, 50, Descripcion: "EPS"),
            new("UltimoPeriodoCompensado", ["ultimoperiodocompensado", "periodocompensado", "ultimoperiodo", "periodo"],
                TipoColumnaCargue.Fecha, Descripcion: "Último período compensado (fecha; '2026-08' = 1 de agosto)"),
            new("TipoAfiliado", ["tipoafiliado", "tipodeafiliado", "tipoafiliacion"],
                TipoColumnaCargue.Texto, 50, EntraAlFlag: false, Descripcion: "Cotizante, beneficiario, adicional"),
        ],
    };

    public static readonly IReadOnlyList<DefinicionFuenteCargue> Todas =
    [
        Sura, Cajacopi, MutualSer, NuevaEps, Simit, DatosClaro,
        // Fase 2, grupo salud
        Positiva, SaludTotal, Sos, Coosalud, SaviaSalud, MiVacuna,
        // Fase 2, grupo otras
        Fosyga, Rgm, TigoUne, Tigo,
        // Fase 2, grupo empresas
        Rues, Camara, CamaraEmpresa, PositivaEmpresas, ArlSura, Compensados,
    ];

    public static DefinicionFuenteCargue? Buscar(string? fuente) =>
        string.IsNullOrWhiteSpace(fuente)
            ? null
            : Todas.FirstOrDefault(f => f.Fuente.Equals(fuente.Trim(), StringComparison.OrdinalIgnoreCase));
}

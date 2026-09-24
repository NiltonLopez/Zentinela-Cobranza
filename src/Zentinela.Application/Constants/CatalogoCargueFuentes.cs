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
/// <param name="DesdeArchivo">false = la columna no se lee del archivo: la produce la regla
/// (SIMIT: <c>Placa</c> sale de Placa1..15 en <c>Expandir</c>). No se busca, no se pide y no se
/// lista como faltante.</param>
public sealed record ColumnaFuenteCargue(
    string Nombre,
    string[] Alias,
    TipoColumnaCargue Tipo,
    int Largo = 0,
    bool Obligatoria = false,
    bool EntraAlFlag = true,
    bool EntraAClave = true,
    string Descripcion = "",
    bool DesdeArchivo = true);

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
                TipoColumnaCargue.Entero, Descripcion: "Año del modelo (se anula si no es un año plausible)"),
            new("Placa", ["placa", "placavehiculo", "numeroplaca"],
                TipoColumnaCargue.Placa, 50, Descripcion: "Placa (sin espacios ni guiones; lo que no es placa se anula)"),
        ],
    };

    public static readonly IReadOnlyList<DefinicionFuenteCargue> Todas = [Sura, Cajacopi, MutualSer, NuevaEps, Simit, DatosClaro, Fosyga, Rgm];

    public static DefinicionFuenteCargue? Buscar(string? fuente) =>
        string.IsNullOrWhiteSpace(fuente)
            ? null
            : Todas.FirstOrDefault(f => f.Fuente.Equals(fuente.Trim(), StringComparison.OrdinalIgnoreCase));
}

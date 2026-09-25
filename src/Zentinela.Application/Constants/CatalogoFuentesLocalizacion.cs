namespace Zentinela.Application;

/// <summary>
/// Estado operativo de una fuente de localización. Es una etiqueta que mantiene el equipo (no se
/// calcula): 'Funcional' = se actualiza con regularidad; 'Intermitente' = irregular; 'Inactivo' =
/// sin actualizaciones recientes.
/// </summary>
public enum EstadoFuente { Funcional, Intermitente, Inactivo }

/// <summary>Qué tipo de dato aporta la fuente, para agrupar en los informes.</summary>
public enum GrupoFuente { Contacto, Afiliacion, Laboral, Vehiculo }

/// <summary>
/// Una fuente de localización (ADR-040).
/// </summary>
/// <param name="Clave">Igual a <see cref="DefinicionFuenteCargue.Fuente"/> de <see cref="CatalogoCargueFuentes"/>.</param>
/// <param name="EtiquetaCalificador">Literal exacto con que el Calificador nombra la fila en
/// "Porcentajes de Localización" (<c>CalificadorCarteraService.EtiquetasLocalizacion</c>).</param>
/// <param name="Slug">Slug de la descarga por celda del Calificador (ADR-019).</param>
/// <param name="Tabla">Tabla fuente (<c>dbo.X</c>). Identificador SQL: solo desde este catálogo.</param>
/// <param name="ColumnaFecha">Columna que ordena la "última fila" por titular (<c>ROW_NUMBER ... DESC</c>).</param>
/// <param name="PredicadoEncontrado">Condición SQL sobre las columnas ORIGEN de la tabla con alias
/// <c>s.</c>, aplicada a la última fila del titular. Traducción 1:1 de lo que cuenta el result set 9
/// del Calificador sobre las columnas que proyecta <c>dbo.fn_BaseMadreParaTitulares</c> (ver
/// <see cref="CatalogoFuentesLocalizacion"/>).</param>
/// <param name="Estado">Etiqueta operativa que mantiene el equipo.</param>
/// <param name="Grupo">Tipo de dato que aporta.</param>
/// <param name="Nota">Aclaración que el Calificador muestra junto al estado (o null).</param>
/// <param name="TitularIdComoTexto">La tabla guarda el TitularID como varchar (NuevaEps): hay que
/// cruzar con <c>TRY_CAST(s.TitularID AS bigint)</c>, como hace la función de la BaseMadre.</param>
/// <param name="EnPanelCalificador">Tiene fila en "Porcentajes de Localización" del Calificador.</param>
/// <param name="ColumnaClave">Columna que identifica al titular en la tabla. "TitularID" salvo
/// CamaraEmpresa (NIT de la empresa), que por eso no se puede medir por titular.</param>
public sealed record FuenteLocalizacion(
    string Clave,
    string EtiquetaCalificador,
    string Slug,
    string Tabla,
    string ColumnaFecha,
    string PredicadoEncontrado,
    EstadoFuente Estado,
    GrupoFuente Grupo,
    string? Nota = null,
    bool TitularIdComoTexto = false,
    bool EnPanelCalificador = true,
    string ColumnaClave = "TitularID");

/// <summary>
/// Catálogo de fuentes de localización (ADR-040): fuente única de verdad del estado de cada fuente
/// (antes vivía solo en el front del Calificador, <c>ESTADO_FUENTE</c>) y de qué cuenta como
/// "encontrado" en cada una. El Calificador lo lee por <c>GET /api/fuentes-localizacion</c>; los
/// Informes de Localización arman su SQL a partir de él.
///
/// <para><b>Traducción de los predicados.</b> El Calificador evalúa el "encontrado" sobre las
/// columnas que proyecta <c>dbo.fn_BaseMadreParaTitulares</c>, que envuelve casi todo en
/// <c>ISNULL(col, 0)</c> (verificado con <c>OBJECT_DEFINITION</c> el 2026-09-25). Aquí se evalúa
/// directo sobre la columna origen, y es equivalente fila por fila:</para>
/// <list type="bullet">
/// <item>Texto: <c>NULLIF(NULLIF(ISNULL(x,0),''),'0')</c> ≡ <c>NULLIF(NULLIF(x,''),'0')</c>, porque
/// un NULL origen termina en NULL en los dos lados (el 0 del ISNULL se convierte a '0' por ser
/// varchar la columna).</item>
/// <item>Número (NIT bigint, Modelo int): <c>ISNULL(x,0) != 0</c> ≡ <c>x IS NOT NULL AND x != 0</c>.</item>
/// <item>Edad (Cajacopi, Positiva): la función proyecta la edad calculada desde
/// <c>FechaNacimiento</c> con <c>ISNULL(..., 0)</c> y el Calificador cuenta "edad != 0"; aquí se
/// repite la misma expresión de edad sobre <c>s.FechaNacimiento</c>, con el NULL explícito.</item>
/// </list>
/// <para>Si cambia el result set 9 del Calificador o la función, hay que cambiar esto (y al revés):
/// los tests de <c>CatalogoFuentesLocalizacionTests</c> vigilan etiquetas, slugs, tablas y columnas.</para>
/// </summary>
public static class CatalogoFuentesLocalizacion
{
    // ── Piezas de predicado ──────────────────────────────────────────────────

    /// <summary>Texto con valor: ni NULL, ni '', ni '0'.</summary>
    private static string Texto(string col) => $"NULLIF(NULLIF(s.{col},''),'0') IS NOT NULL";

    /// <summary>Número con valor: ni NULL ni 0.</summary>
    private static string Numero(string col) => $"(s.{col} IS NOT NULL AND s.{col} != 0)";

    /// <summary>
    /// Edad distinta de 0 calculada desde la fecha de nacimiento: la MISMA expresión de edad de
    /// fn_BaseMadreParaTitulares ([Edad CajaCopi], [Edad Positiva]), incluido el GETDATE().
    /// </summary>
    private static string Edad(string col) =>
        $"(s.{col} IS NOT NULL AND (DATEDIFF(YEAR, s.{col}, GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, s.{col}, GETDATE()), s.{col}) > GETDATE() THEN 1 ELSE 0 END) != 0)";

    private static string O(params string[] partes) => string.Join(" OR ", partes);

    // ── Fuentes (orden = orden de filas del panel del Calificador) ───────────

    public static readonly IReadOnlyList<FuenteLocalizacion> Todas =
    [
        new("Cajacopi", "Cajacopi", "cajacopi", "dbo.Cajacopi", "FechaConsulta",
            O(Texto("NumeroTelefonoCelular"), Texto("Email"), Edad("FechaNacimiento")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        new("Camara", "Cámara", "camara", "dbo.Camara", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email")),
            EstadoFuente.Intermitente, GrupoFuente.Contacto),

        new("SaludTotal", "Salud Total", "salud-total", "dbo.SaludTotal", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email"), Numero("NIT"), Texto("Empresa")),
            EstadoFuente.Inactivo, GrupoFuente.Contacto),

        // Fosyga pasa a Funcional por pedido del usuario (ADR-040): ya se carga por el Cargue de Fuentes.
        new("Fosyga", "Fosyga", "fosyga", "dbo.Fosyga", "FechaConsulta",
            O(Texto("EPS"), Texto("Regimen"), Texto("TipoAfiliado")),
            EstadoFuente.Funcional, GrupoFuente.Afiliacion),

        new("Compensados", "Compensados", "compensados", "dbo.Compensados", "FechaConsulta",
            O(Texto("EPS"), Texto("TipoAfiliado")),
            EstadoFuente.Intermitente, GrupoFuente.Afiliacion),

        new("Positiva", "Positiva", "positiva", "dbo.Positiva", "FechaConsulta",
            O(Texto("NumeroTelefonoCelular"), Texto("Email"), Edad("FechaNacimiento")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        new("Tigo", "Tigo", "tigo", "dbo.Tigo", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email")),
            EstadoFuente.Inactivo, GrupoFuente.Contacto),

        new("TigoUne", "TigoUne", "tigoune", "dbo.TigoUne", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        // [Departamento Coosalud] / [Municipio Coosalud] son DepartamentoOrigen / MunicipioOrigen.
        new("Coosalud", "Coosalud", "coosalud", "dbo.Coosalud", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email"), Texto("DepartamentoOrigen"), Texto("MunicipioOrigen")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        // [Estado Afiliacion SaviaSalud] es EstadoDeAfiliacion. NumeroTelefono NO cuenta (la
        // BaseMadre no lo proyecta).
        new("SaviaSalud", "Savia Salud", "saviasalud", "dbo.SaviaSalud", "FechaConsulta",
            O(Texto("NumeroCelular"), Texto("Regimen"), Texto("EstadoDeAfiliacion")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        new("Sura", "Sura", "sura", "dbo.Sura", "FechaConsulta",
            O(Texto("NumeroCelular"), Texto("Email"), Texto("Empresa"), Texto("EstadoAfiliacion")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        // [Celular SOS] = Telefono, [Nit Sos] = NitEmpresa, [Empresa SOS] = RazonSocial.
        new("SOS", "SOS", "sos", "dbo.SOS", "FechaConsulta",
            O(Texto("Telefono"), Numero("NitEmpresa"), Texto("RazonSocial")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        // Única fuente con FechaInsercion (la tabla no tiene FechaConsulta).
        new("DatosClaro", "Claro", "claro", "dbo.DatosClaro", "FechaInsercion",
            O(Texto("NumeroTelefono"), Texto("Email")),
            EstadoFuente.Inactivo, GrupoFuente.Contacto),

        new("MiVacuna", "MiVacuna", "mivacuna", "dbo.MiVacuna", "FechaConsulta",
            O(Texto("Celular"), Texto("Email")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        new("MutualSer", "MutualSer", "mutualser", "dbo.MutualSer", "FechaConsulta",
            O(Texto("NumeroTelefonoCelular"), Texto("NumeroTelefonoCelular2"), Texto("Email"),
              Texto("Departamento"), Texto("Municipio")),
            EstadoFuente.Funcional, GrupoFuente.Contacto),

        // TitularID varchar(30): la BaseMadre cruza con TRY_CAST(TitularID AS BIGINT).
        new("NuevaEps", "NuevaEps", "nuevaeps", "dbo.NuevaEps", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Departamento"), Texto("Municipio")),
            EstadoFuente.Funcional, GrupoFuente.Contacto, TitularIdComoTexto: true),

        new("ARLSura", "ARL Sura", "arlsura", "dbo.ARLSura", "FechaConsulta",
            O(Numero("NIT"), Texto("Empresa")),
            EstadoFuente.Funcional, GrupoFuente.Laboral, Nota: "solo nombre de empresa"),

        new("PositivaEmpresas", "Positiva Emp.", "positiva-empresa", "dbo.PositivaEmpresas", "FechaConsulta",
            O(Numero("NIT"), Texto("Empresa")),
            EstadoFuente.Funcional, GrupoFuente.Laboral),

        new("RUES", "RUES", "rues", "dbo.RUES", "FechaConsulta",
            Texto("NombreEmpresa"),
            EstadoFuente.Funcional, GrupoFuente.Laboral),

        new("SIMIT", "SIMIT", "simit", "dbo.SIMIT", "FechaConsulta",
            Texto("Placa"),
            EstadoFuente.Funcional, GrupoFuente.Vehiculo),

        new("RGM", "RGM", "rgm", "dbo.RGM", "FechaConsulta",
            O(Texto("Marca"), Numero("Modelo"), Texto("Placa")),
            EstadoFuente.Funcional, GrupoFuente.Vehiculo),

        // Fuera del panel: su clave es el NIT de la empresa, no el titular (se llega a ella por
        // ARLSura.NIT, vw_DatosCamaraEmpresa). Mismo proveedor que Cámara (personas), de ahí el
        // estado. El predicado es el del flag del cargue (teléfono o email).
        new("CamaraEmpresa", "Cámara Empresa", "camara-empresa", "dbo.CamaraEmpresa", "FechaConsulta",
            O(Texto("NumeroTelefono"), Texto("Email")),
            EstadoFuente.Intermitente, GrupoFuente.Laboral,
            EnPanelCalificador: false, ColumnaClave: "NitEmpresa"),
    ];

    /// <summary>Las 21 fuentes con fila en el panel del Calificador, en su orden.</summary>
    public static IEnumerable<FuenteLocalizacion> DelPanel => Todas.Where(f => f.EnPanelCalificador);

    public static FuenteLocalizacion? Buscar(string? clave) =>
        string.IsNullOrWhiteSpace(clave)
            ? null
            : Todas.FirstOrDefault(f => f.Clave.Equals(clave.Trim(), StringComparison.OrdinalIgnoreCase));

    public static FuenteLocalizacion? BuscarPorSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug)
            ? null
            : Todas.FirstOrDefault(f => f.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));
}

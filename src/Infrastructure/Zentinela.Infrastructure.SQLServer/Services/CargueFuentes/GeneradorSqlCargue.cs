using System.Text;
using System.Text.RegularExpressions;
using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// Construye TODO el SQL del cargue a partir de la <see cref="DefinicionFuenteCargue"/>.
///
/// Reglas que no se negocian (cada una corrige un defecto real de los SP legacy):
/// <list type="bullet">
/// <item>Identificadores SQL solo desde la definición (código), validados y entre corchetes.
///   Nada del archivo ni del usuario llega a un identificador; los valores van por parámetro o por #temp.</item>
/// <item>INSERT siempre con lista de columnas: los SP posicionales se rompían al agregar una columna.</item>
/// <item>Clave de contenido con <c>EXISTS (SELECT a,b INTERSECT SELECT c,d)</c>: es la igualdad
///   NULL-safe de T-SQL. Con <c>=</c> toda fila con algún NULL quedaría como "nueva" y la tabla
///   se duplicaría en cada carga.</item>
/// <item>Hash de reversión con centinela por columna (<c>ISNULL(..., N'~')</c>): <c>CONCAT_WS</c>
///   ignora los NULL, así que ('a', NULL, 'b') y ('a', 'b', NULL) colisionarían.</item>
/// </list>
/// Las tablas #temp no llevan constraints con nombre: en tempdb el nombre de un constraint es
/// global, y dos cargues simultáneos chocarían.
/// </summary>
internal static class GeneradorSqlCargue
{
    /// <summary>Columna de titular en las tablas destino.</summary>
    public const string ColumnaTitularDestino = "TitularID";

    /// <summary>Largo de <c>Titular.Nombre</c>.</summary>
    public const int LargoNombre = 70;

    // Columnas fijas de #Carga: una columna de dato con uno de estos nombres rompería el DDL.
    private static readonly HashSet<string> ColumnasReservadas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fila", "TitularID", "Nombre", "ReparaNombre", "Refrescada", "Vacia", "Accion",
    };

    private static readonly Regex Identificador = new(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    private const string Reversion =
        "dbo.CargueFuenteReversion (EjecucionID, TitularID, HashContenido, Accion, FechaAnterior)";

    // ── Identificadores y tipos ─────────────────────────────────────────────

    /// <summary>[nombre], o excepción si no es un identificador simple.</summary>
    public static string Id(string nombre) =>
        Identificador.IsMatch(nombre)
            ? $"[{nombre}]"
            : throw new InvalidOperationException($"Identificador SQL inválido en el catálogo de cargue: '{nombre}'.");

    /// <summary>"dbo.Sura" → "[dbo].[Sura]".</summary>
    public static string Tabla(string tablaDestino)
    {
        var partes = tablaDestino.Split('.');
        return partes.Length switch
        {
            1 => $"[dbo].{Id(partes[0])}",
            2 => $"{Id(partes[0])}.{Id(partes[1])}",
            _ => throw new InvalidOperationException($"Tabla destino inválida en el catálogo de cargue: '{tablaDestino}'."),
        };
    }

    public static string TipoSql(ColumnaFuenteCargue c) => c.Tipo switch
    {
        TipoColumnaCargue.Entero => "bigint",
        TipoColumnaCargue.Fecha  => "date",
        _ when c.Largo > 0       => $"varchar({c.Largo}) COLLATE DATABASE_DEFAULT",
        _ => throw new InvalidOperationException($"La columna '{c.Nombre}' ({c.Tipo}) necesita Largo > 0."),
    };

    /// <summary>Valida la definición contra lo que el generador puede producir. Lanza si no cuadra.</summary>
    public static void ValidarDefinicion(DefinicionFuenteCargue d)
    {
        Tabla(d.TablaDestino);
        Id(d.ColumnaFecha);
        if (d.ColumnaFlag is not null) Id(d.ColumnaFlag);
        if (d.ColumnasDato.Count == 0)
            throw new InvalidOperationException($"La fuente {d.Fuente} no tiene columnas de dato.");
        if (!d.ColumnasDato.Any(c => c.EntraAClave))
            throw new InvalidOperationException($"La fuente {d.Fuente} no tiene columnas en la clave de contenido.");

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in d.ColumnasDato)
        {
            Id(c.Nombre);
            TipoSql(c);
            if (ColumnasReservadas.Contains(c.Nombre) || !vistos.Add(c.Nombre)
                || c.Nombre.Equals(d.ColumnaFlag, StringComparison.OrdinalIgnoreCase)
                || c.Nombre.Equals(d.ColumnaFecha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Columna '{c.Nombre}' repetida o reservada en la fuente {d.Fuente}.");
        }
    }

    private static bool UsaVacias(DefinicionFuenteCargue d) => d.AplicaPoliticaVacias && d.ColumnaFlag is not null;

    private static IEnumerable<ColumnaFuenteCargue> Clave(DefinicionFuenteCargue d) => d.ColumnasDato.Where(c => c.EntraAClave);

    private static string Lista(IEnumerable<ColumnaFuenteCargue> cols, string alias) =>
        string.Join(", ", cols.Select(c => $"{alias}.{Id(c.Nombre)}"));

    /// <summary>
    /// Predicado de titular entre destino (s) y #Carga (c). Con TitularID texto en destino
    /// (NuevaEps) se convierte el lado de #Carga: la columna del destino queda sargable.
    /// </summary>
    private static string MismoTitular(DefinicionFuenteCargue d, string destino, string carga) =>
        d.TitularIdComoTexto
            ? $"{destino}.{Id(ColumnaTitularDestino)} = CONVERT(varchar(30), {carga}.TitularID)"
            : $"{destino}.{Id(ColumnaTitularDestino)} = {carga}.TitularID";

    /// <summary>Igualdad NULL-safe de la clave de contenido entre destino (s) y #Carga (c).</summary>
    public static string MismoContenido(DefinicionFuenteCargue d, string destino, string carga) =>
        $"EXISTS (SELECT {Lista(Clave(d), destino)} INTERSECT SELECT {Lista(Clave(d), carga)})";

    /// <summary>SHA-256 de las columnas de dato de una fila del destino (deleted / inserted).</summary>
    public static string HashContenido(DefinicionFuenteCargue d, string pseudo)
    {
        var partes = d.ColumnasDato
            .Select(c => $"ISNULL(CONVERT(nvarchar(400), {pseudo}.{Id(c.Nombre)}), N'~')")
            .ToList();
        // CONCAT_WS exige al menos dos valores; con una sola columna (SIMIT: Placa) va sola.
        var texto = partes.Count == 1 ? partes[0] : $"CONCAT_WS(N'|', {string.Join(", ", partes)})";
        return $"HASHBYTES('SHA2_256', {texto})";
    }

    // ── DDL de trabajo ──────────────────────────────────────────────────────

    /// <summary>
    /// #Carga: una fila por fila destino candidata. Cartera/Líder NO van aquí sino en
    /// #Relaciones: con la deduplicación por titular, la relación de una fila descartada
    /// (mismo titular en otra cartera) se perdería.
    /// </summary>
    public static string CrearTablaCarga(DefinicionFuenteCargue d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DROP TABLE IF EXISTS #Carga;");
        sb.AppendLine("CREATE TABLE #Carga (");
        sb.AppendLine("    Fila         int NOT NULL,");
        sb.AppendLine("    TitularID    bigint NOT NULL,");
        sb.AppendLine($"    Nombre       varchar({LargoNombre}) COLLATE DATABASE_DEFAULT NULL,");
        sb.AppendLine("    ReparaNombre bit NOT NULL,");
        foreach (var c in d.ColumnasDato)
            sb.AppendLine($"    {Id(c.Nombre)} {TipoSql(c)} NULL,");
        if (d.ColumnaFlag is not null)
            sb.AppendLine($"    {Id(d.ColumnaFlag)} bit NOT NULL,");
        sb.AppendLine("    Refrescada   bit NOT NULL DEFAULT (0),");
        sb.AppendLine("    Vacia        bit NOT NULL DEFAULT (0),");
        sb.AppendLine("    PRIMARY KEY CLUSTERED (TitularID, Fila)");
        sb.AppendLine(");");
        return sb.ToString();
    }

    public const string CrearTablasApoyo = """
        DROP TABLE IF EXISTS #Titulares;
        CREATE TABLE #Titulares (TitularID bigint NOT NULL PRIMARY KEY CLUSTERED);
        DROP TABLE IF EXISTS #Relaciones;
        CREATE TABLE #Relaciones (
            TitularID bigint NOT NULL,
            LiderID   int    NOT NULL,
            CarteraID int    NOT NULL,
            PRIMARY KEY CLUSTERED (TitularID, LiderID, CarteraID)
        );
        """;

    /// <summary>
    /// Existencia y nombre actual de cada titular del archivo. El "nombre roto" se decide en
    /// C# (<c>NormalizadorTitular.EstaRoto</c>), NUNCA con <c>LIKE '%Ã%'</c>: con collation
    /// insensible a acentos, 'Ã' matchea cualquier 'A'.
    /// </summary>
    public const string ConsultarTitulares = """
        SELECT x.TitularID,
               CAST(CASE WHEN t.NumeroDocumento IS NULL THEN 0 ELSE 1 END AS bit) AS Existe,
               t.Nombre
          FROM #Titulares x
          LEFT JOIN dbo.Titular t ON t.NumeroDocumento = x.TitularID;
        """;

    // Relaciones nuevas: solo pares ACTIVOS en Lider_Cartera (EXISTS y no JOIN: si Lider_Cartera
    // tuviera el par repetido, el JOIN duplicaría filas y violaría la PK de Titular_Cartera).
    private const string OrigenRelacionesNuevas = """
          FROM #Relaciones r
         WHERE EXISTS (SELECT 1 FROM dbo.Lider_Cartera lc
                        WHERE lc.LiderID = r.LiderID AND lc.CarteraID = r.CarteraID AND lc.Activo = 1)
           AND NOT EXISTS (SELECT 1 FROM dbo.Titular_Cartera tc
                            WHERE tc.TitularID = r.TitularID AND tc.LiderID = r.LiderID AND tc.CarteraID = r.CarteraID)
        """;

    public const string ContarRelacionesNuevas = "SELECT COUNT(*)\n" + OrigenRelacionesNuevas + ";";

    /// <summary>
    /// Marca en #Carga qué filas solo refrescan fecha y cuáles son vacías descartables. Se
    /// ejecuta en la validación (para el preview) y otra vez DENTRO de la transacción de
    /// escritura, para que los conteos que se verifican sean los del momento de escribir.
    /// </summary>
    public static string MarcarImpacto(DefinicionFuenteCargue d)
    {
        var destino = Tabla(d.TablaDestino);
        var sb = new StringBuilder();
        sb.AppendLine("UPDATE #Carga SET Refrescada = 0, Vacia = 0;");
        sb.AppendLine($"""
            UPDATE c SET Refrescada = 1
              FROM #Carga c
             WHERE EXISTS (SELECT 1 FROM {destino} s
                            WHERE {MismoTitular(d, "s", "c")}
                              AND {MismoContenido(d, "s", "c")});
            """);
        if (UsaVacias(d))
        {
            var flag = Id(d.ColumnaFlag!);
            sb.AppendLine($"""
                UPDATE c SET Vacia = 1
                  FROM #Carga c
                 WHERE c.{flag} = 0
                   AND (EXISTS (SELECT 1 FROM {destino} s WHERE {MismoTitular(d, "s", "c")} AND s.{flag} = 1)
                     OR EXISTS (SELECT 1 FROM #Carga c2 WHERE c2.TitularID = c.TitularID AND c2.{flag} = 1));
                """);
        }
        return sb.ToString();
    }

    /// <summary>Candidatas, Refrescadas, DescartadasVacias, Insertadas, VaciasQuePurgara.</summary>
    public static string ConsultarImpacto(DefinicionFuenteCargue d)
    {
        var vaciasQuePurgara = UsaVacias(d)
            ? $"""
               (SELECT COUNT(*) FROM {Tabla(d.TablaDestino)} s
                 WHERE s.{Id(d.ColumnaFlag!)} = 0
                   AND EXISTS (SELECT 1 FROM #Carga c WHERE {MismoTitular(d, "s", "c")} AND c.{Id(d.ColumnaFlag!)} = 1))
               """
            : "0";
        return $"""
            SELECT COUNT(*) AS Candidatas,
                   ISNULL(SUM(CASE WHEN Refrescada = 1 AND Vacia = 0 THEN 1 ELSE 0 END), 0) AS Refrescadas,
                   ISNULL(SUM(CASE WHEN Vacia = 1 THEN 1 ELSE 0 END), 0)                    AS DescartadasVacias,
                   ISNULL(SUM(CASE WHEN Refrescada = 0 AND Vacia = 0 THEN 1 ELSE 0 END), 0) AS Insertadas,
                   {vaciasQuePurgara} AS VaciasQuePurgara
              FROM #Carga;
            """;
    }

    /// <summary>Primeras filas de #Carga ya limpias, con la acción que tendrán. Columnas: <see cref="ColumnasMuestra"/>.</summary>
    public static string ConsultarMuestra(DefinicionFuenteCargue d)
    {
        var cols = new List<string> { "c.TitularID", "c.Nombre" };
        cols.AddRange(d.ColumnasDato.Select(c => $"c.{Id(c.Nombre)}"));
        if (d.ColumnaFlag is not null) cols.Add($"c.{Id(d.ColumnaFlag)}");
        cols.Add("""
            CASE WHEN c.Vacia = 1 THEN 'Descartar (vacía)'
                 WHEN c.Refrescada = 1 THEN 'Refrescar fecha'
                 ELSE 'Insertar' END AS Accion
            """);
        return $"SELECT TOP (@Filas) {string.Join(", ", cols)} FROM #Carga c ORDER BY c.Fila;";
    }

    public static List<string> ColumnasMuestra(DefinicionFuenteCargue d)
    {
        var cols = new List<string> { "TitularID", "Nombre" };
        cols.AddRange(d.ColumnasDato.Select(c => c.Nombre));
        if (d.ColumnaFlag is not null) cols.Add(d.ColumnaFlag);
        cols.Add("Acción");
        return cols;
    }

    // ── Escritura (dentro de la transacción; parámetros @Sello y @EjecucionID) ─

    /// <summary>Selecciona el sello del servidor SQL (no del servidor de la API).</summary>
    public static string LeerSello(DefinicionFuenteCargue d) =>
        d.TipoFecha == TipoFechaSello.Date
            ? "SELECT CAST(GETDATE() AS date);"
            : "SELECT GETDATE();";

    /// <summary>1. Titulares nuevos. MAX(Nombre) rompe empates entre filas del mismo titular.</summary>
    public const string InsertarTitulares = """
        INSERT INTO dbo.Titular (NumeroDocumento, Nombre)
        SELECT c.TitularID, MAX(c.Nombre)
          FROM #Carga c
         WHERE c.Nombre IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.Titular t WHERE t.NumeroDocumento = c.TitularID)
         GROUP BY c.TitularID;
        """;

    /// <summary>2. Reparar SOLO los nombres que C# marcó como rotos en BD y limpios en el archivo.</summary>
    public const string RepararNombres = """
        UPDATE t SET t.Nombre = x.Nombre
          FROM dbo.Titular t
          JOIN (SELECT TitularID, MAX(Nombre) AS Nombre
                  FROM #Carga
                 WHERE ReparaNombre = 1 AND Nombre IS NOT NULL
                 GROUP BY TitularID) x ON x.TitularID = t.NumeroDocumento
         WHERE t.Nombre <> x.Nombre;
        """;

    /// <summary>3. Titular_Cartera, solo pares activos.</summary>
    public const string InsertarRelaciones =
        "INSERT INTO dbo.Titular_Cartera (TitularID, LiderID, CarteraID)\nSELECT r.TitularID, r.LiderID, r.CarteraID\n"
        + OrigenRelacionesNuevas + ";";

    /// <summary>4. Política de vacías: no entra una fila vacía de un titular que tiene datos.</summary>
    public const string DescartarVacias = "DELETE FROM #Carga WHERE Vacia = 1;";

    /// <summary>
    /// 5. Contenido idéntico → solo se refresca la fecha, guardando la anterior para revertir.
    /// EXISTS y no JOIN: una fila destino se actualiza una vez aunque la emparejen varias de #Carga.
    /// </summary>
    public static string Refrescar(DefinicionFuenteCargue d)
    {
        var fecha = Id(d.ColumnaFecha);
        return $"""
            UPDATE s SET s.{fecha} = @Sello
            OUTPUT @EjecucionID, CONVERT(varchar(30), deleted.{Id(ColumnaTitularDestino)}),
                   {HashContenido(d, "deleted")}, 'U', deleted.{fecha}
              INTO {Reversion}
              FROM {Tabla(d.TablaDestino)} s
             WHERE EXISTS (SELECT 1 FROM #Carga c
                            WHERE c.Refrescada = 1
                              AND {MismoTitular(d, "s", "c")}
                              AND {MismoContenido(d, "s", "c")});
            """;
    }

    /// <summary>
    /// Refrescadas se cuentan del lado de #Carga y no con @@ROWCOUNT del UPDATE: el destino
    /// tiene duplicados legacy (los SP deduplicaban tarde), y una fila del archivo puede
    /// refrescar varias filas destino idénticas.
    /// </summary>
    public const string ContarRefrescadas = "SELECT COUNT(*) FROM #Carga WHERE Refrescada = 1;";

    /// <summary>6. Contenido nuevo → INSERT con lista de columnas.</summary>
    public static string Insertar(DefinicionFuenteCargue d)
    {
        var destinoCols = new List<string> { Id(ColumnaTitularDestino) };
        var origenCols = new List<string>
        {
            d.TitularIdComoTexto ? "CONVERT(varchar(30), c.TitularID)" : "c.TitularID",
        };
        foreach (var c in d.ColumnasDato)
        {
            destinoCols.Add(Id(c.Nombre));
            origenCols.Add($"c.{Id(c.Nombre)}");
        }
        if (d.ColumnaFlag is not null)
        {
            destinoCols.Add(Id(d.ColumnaFlag));
            origenCols.Add($"c.{Id(d.ColumnaFlag)}");
        }
        destinoCols.Add(Id(d.ColumnaFecha));
        origenCols.Add("@Sello");

        return $"""
            INSERT INTO {Tabla(d.TablaDestino)} ({string.Join(", ", destinoCols)})
            OUTPUT @EjecucionID, CONVERT(varchar(30), inserted.{Id(ColumnaTitularDestino)}),
                   {HashContenido(d, "inserted")}, 'I', NULL
              INTO {Reversion}
            SELECT {string.Join(", ", origenCols)}
              FROM #Carga c
             WHERE c.Refrescada = 0;
            """;
    }

    /// <summary>
    /// 7. Purgar las vacías viejas del titular que ahora trae datos (equivale al
    /// DELETE Estado = 0 de los SP). Hash 0x00: así lo documenta CargueFuenteReversion para 'D'.
    /// null si la fuente no aplica política de vacías.
    /// </summary>
    public static string? PurgarVacias(DefinicionFuenteCargue d)
    {
        if (!UsaVacias(d)) return null;
        var flag = Id(d.ColumnaFlag!);
        return $"""
            DELETE s
            OUTPUT @EjecucionID, CONVERT(varchar(30), deleted.{Id(ColumnaTitularDestino)}), 0x00, 'D', deleted.{Id(d.ColumnaFecha)}
              INTO {Reversion}
              FROM {Tabla(d.TablaDestino)} s
             WHERE s.{flag} = 0
               AND EXISTS (SELECT 1 FROM #Carga c WHERE {MismoTitular(d, "s", "c")} AND c.{flag} = 1);
            """;
    }

    // ── Auditoría ───────────────────────────────────────────────────────────

    public const string RegistrarInicio = """
        INSERT INTO dbo.CargueFuenteEjecucion (JobID, Fuente, Usuario, NombreArchivo, HuellaArchivo, Estado)
        OUTPUT inserted.EjecucionID
        VALUES (@JobID, @Fuente, @Usuario, @NombreArchivo, @Huella, 'EN CURSO');
        """;

    public const string RegistrarFinOk = """
        UPDATE dbo.CargueFuenteEjecucion
           SET Estado = 'OK', FechaFin = SYSDATETIME(), SelloFechaConsulta = @Sello,
               TotalLeidas = @TotalLeidas, Excluidas = @Excluidas, DuplicadasArchivo = @DuplicadasArchivo,
               Candidatas = @Candidatas, Insertadas = @Insertadas, Refrescadas = @Refrescadas,
               DescartadasVacias = @DescartadasVacias, VaciasPurgadas = @VaciasPurgadas,
               TitularesNuevos = @TitularesNuevos, NombresReparados = @NombresReparados,
               RelacionesNuevas = @RelacionesNuevas, Advertencias = @Advertencias, Error = NULL
         WHERE EjecucionID = @EjecucionID;
        """;

    public const string RegistrarFinFallo = """
        UPDATE dbo.CargueFuenteEjecucion
           SET Estado = @Estado, FechaFin = SYSDATETIME(), Error = @Error
         WHERE EjecucionID = @EjecucionID AND Estado = 'EN CURSO';
        """;
}

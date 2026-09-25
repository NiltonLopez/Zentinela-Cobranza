using System.Text;
using System.Text.RegularExpressions;
using Zentinela.Application;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// SQL de los envíos a localización (ADR-040, etapa 3). Los nombres de las tablas propias
/// (<c>LocalizacionEnvio</c>, <c>LocalizacionEnvioDetalle</c> y, para la cola, <c>LocalizacionCorte</c> /
/// <c>LocalizacionResumenFuente</c>) se inyectan con un prefijo (<see cref="TablasEnvio.ConPrefijo"/>): <c>dbo.</c>
/// en producción, <c>#</c> para correr el MISMO SQL contra tablas temporales de la sesión (así se probó antes de
/// que existieran las tablas). Las tablas que solo se leen (fuentes, <c>UltimoEstadoTitular</c>, <c>Pago</c>,
/// <c>CargueFuenteEjecucion</c>, <c>Titular_Cartera</c>…) van siempre con <c>dbo.</c>.
///
/// <para>Todo es parametrizado; lo único que se interpola son identificadores del catálogo (validados) y
/// los códigos de estado de <see cref="TransicionesEnvio"/>.</para>
/// </summary>
internal static class GeneradorSqlEnvios
{
    private static readonly Regex Prefijo = new("^(dbo\\.|#)$", RegexOptions.Compiled);
    private static readonly Regex Identificador = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex TablaDbo = new("^dbo\\.[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Tablas propias del módulo, con el prefijo de producción (<c>dbo.</c>) o de prueba (<c>#</c>).</summary>
    public sealed record TablasEnvio(string Envio, string Detalle, string Corte, string ResumenFuente)
    {
        public static TablasEnvio ConPrefijo(string prefijo)
        {
            if (!Prefijo.IsMatch(prefijo))
                throw new ArgumentException($"Prefijo de tablas no permitido: '{prefijo}'.", nameof(prefijo));
            return new(prefijo + "LocalizacionEnvio", prefijo + "LocalizacionEnvioDetalle",
                       prefijo + "LocalizacionCorte", prefijo + "LocalizacionResumenFuente");
        }
    }

    public static TablasEnvio Produccion { get; } = TablasEnvio.ConPrefijo("dbo.");

    // ════════════════════════════════════════════════════════════════════════
    //  Crear
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un lote BORRADOR. Parámetros: <c>@LiderID</c>, <c>@CarteraID</c>, <c>@Fuente</c>, <c>@Parametros</c>,
    /// <c>@Observacion</c>, <c>@Usuario</c> y <c>@Filas</c> (JSON <c>[{"t":TitularID,"c":Categoria,"p":Prioridad,"u":UltimaConsulta}]</c>,
    /// las filas que el servidor recalculó).
    ///
    /// <para>Primero, FUERA de la transacción, la foto de cada titular en el par (<c>UltimoEstadoTitular</c> por su
    /// PK) y la verificación de que pertenece al par con relación activa (defensa: la lista ya sale de las
    /// sugerencias del mismo par). Luego una transacción corta inserta cabecera + detalle y fija el conteo:
    /// todo o nada. Devuelve EnvioID (NULL si no quedó ningún titular), Codigo, Titulares y Descartados.</para>
    /// </summary>
    public static string Crear(TablasEnvio t) => $$"""
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        DECLARE @F TABLE (TitularID bigint NOT NULL PRIMARY KEY, Categoria varchar(20) NOT NULL,
                          Prioridad smallint NOT NULL, UltimaConsulta date NULL);
        INSERT INTO @F (TitularID, Categoria, Prioridad, UltimaConsulta)
        SELECT t, c, p, u
          FROM OPENJSON(@Filas) WITH (t bigint '$.t', c varchar(20) '$.c', p smallint '$.p', u date '$.u');

        -- Foto al crear el lote, solo de titulares del par con relación activa. Búsquedas por PK
        -- (Titular_Cartera y UltimoEstadoTitular), como en las sugerencias: con las estadísticas de
        -- IX_UltimoEstado_CarteraID viejas el optimizador puede recorrer la cartera entera por cada titular.
        DECLARE @N TABLE (TitularID bigint NOT NULL PRIMARY KEY, Categoria varchar(20) NOT NULL, Prioridad smallint NOT NULL,
                          UltimaConsulta date NULL, SaldoTotal money NULL, UltimoPerfil varchar(50) NULL,
                          MejorPerfil varchar(50) NULL, FechaUltimaGestion date NULL, FechaPromesa date NULL, ValorPromesa money NULL);
        INSERT INTO @N (TitularID, Categoria, Prioridad, UltimaConsulta, SaldoTotal, UltimoPerfil, MejorPerfil,
                        FechaUltimaGestion, FechaPromesa, ValorPromesa)
        SELECT f.TitularID, f.Categoria, f.Prioridad, f.UltimaConsulta,
               u.SaldoTotal, u.UltimoPerfil, u.MejorPerfil, u.FechaUltimaGestion, u.FechaPromesa, u.ValorPromesa
          FROM @F f
          LEFT JOIN dbo.UltimoEstadoTitular u WITH (INDEX(1), FORCESEEK)
                 ON u.TitularID = f.TitularID AND u.CarteraID = @CarteraID
         WHERE EXISTS (SELECT 1 FROM dbo.Titular_Cartera tc WITH (FORCESEEK)
                        WHERE tc.TitularID = f.TitularID AND tc.LiderID = @LiderID AND tc.CarteraID = @CarteraID)
           AND EXISTS (SELECT 1 FROM dbo.Lider_Cartera lc
                        WHERE lc.LiderID = @LiderID AND lc.CarteraID = @CarteraID AND lc.Activo = 1)
        OPTION (MAXDOP 4, LOOP JOIN);

        DECLARE @Descartados int = (SELECT COUNT(*) FROM @F) - (SELECT COUNT(*) FROM @N);
        DECLARE @EnvioID int = NULL, @Insertadas int = 0;

        IF EXISTS (SELECT 1 FROM @N)
        BEGIN
            BEGIN TRANSACTION;

            INSERT INTO {{t.Envio}} (LiderID, CarteraID, Fuente, Estado, Titulares, Parametros, Observacion, CreadoPor)
            VALUES (@LiderID, @CarteraID, @Fuente, 'BORRADOR', 0, @Parametros, @Observacion, @Usuario);
            SET @EnvioID = CAST(SCOPE_IDENTITY() AS int);

            INSERT INTO {{t.Detalle}} (EnvioID, TitularID, Categoria, Prioridad, UltimaConsultaPrevia, SaldoTotal,
                                       UltimoPerfil, MejorPerfil, FechaUltimaGestion, FechaPromesa, ValorPromesa)
            SELECT @EnvioID, TitularID, Categoria, Prioridad, UltimaConsulta, SaldoTotal,
                   UltimoPerfil, MejorPerfil, FechaUltimaGestion, FechaPromesa, ValorPromesa
              FROM @N;
            SET @Insertadas = @@ROWCOUNT;

            UPDATE {{t.Envio}} SET Titulares = @Insertadas WHERE EnvioID = @EnvioID;

            COMMIT TRANSACTION;
        END

        SELECT @EnvioID AS EnvioID,
               (SELECT Codigo FROM {{t.Envio}} WHERE EnvioID = @EnvioID) AS Codigo,
               @Insertadas AS Titulares,
               @Descartados AS Descartados;
        """;

    // ════════════════════════════════════════════════════════════════════════
    //  Transiciones
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aplica una acción con concurrencia optimista: actualiza solo si el estado en la BD sigue siendo uno de
    /// los de origen (y, para un Líder, si el lote es suyo). Parámetros: <c>@EnvioID</c>, <c>@Usuario</c>,
    /// <c>@Motivo</c> (anular) y <c>@LiderAlcance</c> (null = sin restricción). Devuelve las filas afectadas y el
    /// estado actual (NULL = el lote no existe o no es del alcance).
    /// </summary>
    public static string Transicion(TablasEnvio t, AccionEnvio accion)
    {
        var destino = TransicionesEnvio.Codigo(TransicionesEnvio.Destino(accion));
        var origenes = string.Join(", ", TransicionesEnvio.Origenes(accion).Select(e => $"'{TransicionesEnvio.Codigo(e)}'"));
        var set = accion switch
        {
            AccionEnvio.Enviar => "EnviadoPor = @Usuario, FechaEnvio = SYSDATETIME()",
            AccionEnvio.Cerrar => "CerradoPor = @Usuario, FechaCierre = SYSDATETIME()",
            _                  => "AnuladoPor = @Usuario, FechaAnulacion = SYSDATETIME(), MotivoAnulacion = @Motivo",
        };

        return $"""
            SET NOCOUNT ON;
            DECLARE @Afectadas int;

            UPDATE {t.Envio}
               SET Estado = '{destino}', {set}
             WHERE EnvioID = @EnvioID
               AND Estado IN ({origenes})
               AND (@LiderAlcance IS NULL OR LiderID = @LiderAlcance);
            SET @Afectadas = @@ROWCOUNT;

            SELECT @Afectadas AS Afectadas,
                   (SELECT Estado FROM {t.Envio}
                     WHERE EnvioID = @EnvioID AND (@LiderAlcance IS NULL OR LiderID = @LiderAlcance)) AS EstadoActual,
                   (SELECT Codigo FROM {t.Envio} WHERE EnvioID = @EnvioID) AS Codigo;
            """;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Lecturas
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Columnas de <see cref="Cabeceras"/>, en orden (el servicio las lee por posición).</summary>
    public const string ColumnasCabecera =
        "e.EnvioID, e.Codigo, e.LiderID, l.Nombre, l.NumeroDocumento, e.CarteraID, c.NombreCartera, e.Fuente, e.Estado, "
      + "e.Titulares, e.Parametros, e.Observacion, e.CreadoPor, e.FechaCreacion, e.EnviadoPor, e.FechaEnvio, "
      + "e.CerradoPor, e.FechaCierre, e.AnuladoPor, e.FechaAnulacion, e.MotivoAnulacion, e.CargueEjecucionID";

    /// <summary>
    /// Cabeceras de lotes, los más recientes primero. Todos los filtros son opcionales (NULL = sin filtro):
    /// <c>@EnvioID</c>, <c>@LiderID</c> (el alcance del Líder va aquí), <c>@CarteraID</c>, <c>@Fuente</c>, <c>@Estado</c>,
    /// <c>@Desde</c>/<c>@Hasta</c> (FechaCreacion), <c>@EnvioDesde</c>/<c>@EnvioHasta</c> (FechaEnvio),
    /// <c>@SoloEnviados</c> (ENVIADO o CERRADO) y <c>@Max</c>.
    /// </summary>
    public static string Cabeceras(TablasEnvio t) => $"""
        SELECT TOP (@Max) {ColumnasCabecera}
          FROM {t.Envio} e
          LEFT JOIN dbo.Lider l   ON l.ID = e.LiderID
          LEFT JOIN dbo.Cartera c ON c.ID = e.CarteraID
         WHERE (@EnvioID IS NULL    OR e.EnvioID = @EnvioID)
           AND (@LiderID IS NULL    OR e.LiderID = @LiderID)
           AND (@CarteraID IS NULL  OR e.CarteraID = @CarteraID)
           AND (@Fuente IS NULL     OR e.Fuente = @Fuente)
           AND (@Estado IS NULL     OR e.Estado = @Estado)
           AND (@Desde IS NULL      OR e.FechaCreacion >= @Desde)
           AND (@Hasta IS NULL      OR e.FechaCreacion < @Hasta)
           AND (@EnvioDesde IS NULL OR e.FechaEnvio >= @EnvioDesde)
           AND (@EnvioHasta IS NULL OR e.FechaEnvio < @EnvioHasta)
           AND (@SoloEnviados = 0   OR e.Estado IN ('ENVIADO', 'CERRADO'))
         ORDER BY e.EnvioID DESC
        OPTION (RECOMPILE);
        """;

    /// <summary>Lotes BORRADOR por líder (uso del departamento: lo que un líder armó y no envió).</summary>
    public static string BorradoresPorLider(TablasEnvio t) => $"""
        SELECT LiderID, COUNT(*) FROM {t.Envio} WHERE Estado = 'BORRADOR' GROUP BY LiderID;
        """;

    /// <summary>
    /// Cargues OK del Cargue de Fuentes (candidatos a retorno), sin las corridas del harness (<c>@PatronHarness</c>).
    /// Pocas filas (una por archivo cargado); C# elige el retorno de cada lote.
    /// </summary>
    public const string CarguesOk = """
        SELECT EjecucionID, Fuente, Usuario, Estado, FechaFin, SelloFechaConsulta
          FROM dbo.CargueFuenteEjecucion
         WHERE Estado = 'OK' AND FechaFin IS NOT NULL AND Usuario NOT LIKE @PatronHarness;
        """;

    /// <summary>Titulares del lote para el Excel del cargue, por prioridad.</summary>
    public static string FilasExcel(TablasEnvio t) => $"""
        SELECT d.TitularID, ti.Nombre
          FROM {t.Detalle} d
          LEFT JOIN dbo.Titular ti ON ti.NumeroDocumento = d.TitularID
         WHERE d.EnvioID = @EnvioID
         ORDER BY d.Prioridad DESC, d.TitularID;
        """;

    /// <summary>
    /// Filas del último corte OK por par y fuente Funcional, para la cola aproximada
    /// (<see cref="UsoDepartamentoCalculo.Cola"/>). Result sets: la fecha del corte; las filas.
    /// </summary>
    public static string ColaUltimoCorte(TablasEnvio t, IEnumerable<FuenteLocalizacion> funcionales)
    {
        var lista = string.Join(", ", funcionales.Select(f => $"'{Validar(f.Clave)}'"));
        if (lista.Length == 0) lista = "''";
        return $"""
            SET NOCOUNT ON;
            DECLARE @CorteID int, @FechaCorte datetime2(0);
            SELECT TOP (1) @CorteID = CorteID, @FechaCorte = FechaCorte
              FROM {t.Corte} WHERE Estado = 'OK' ORDER BY CorteID DESC;
            SELECT @FechaCorte AS FechaCorte;
            SELECT LiderID, CarteraID, Total, Buscados, Encontrados, Viejos180
              FROM {t.ResumenFuente}
             WHERE CorteID = @CorteID AND Fuente IN ({lista});
            """;
    }

    /// <summary>Líderes con alguna relación interna activa (filas del uso del departamento aunque no hayan enviado nada).</summary>
    public const string LideresActivos = """
        SELECT DISTINCT l.ID, l.Nombre, l.NumeroDocumento
          FROM dbo.Lider_Cartera lc
          INNER JOIN dbo.Lider l   ON l.ID = lc.LiderID
          INNER JOIN dbo.Cartera c ON c.ID = lc.CarteraID AND c.Activa = 1
         WHERE lc.Activo = 1 AND lc.EsExterna = 0
           AND UPPER(LTRIM(RTRIM(l.Nombre))) <> 'BASE GENERAL';
        """;

    // ════════════════════════════════════════════════════════════════════════
    //  Retorno e impacto
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retorno e impacto de un conjunto de lotes, en un batch. Parámetros:
    /// <list type="bullet">
    /// <item><c>@Lotes</c>: JSON <c>[{"e":EnvioID,"d":Desde,"i":Inclusivo}]</c>. <c>Desde</c> es el umbral de
    ///   "buscado tras el envío" que eligió C# (<see cref="RetornoEnvio.Umbral"/>): el sello del cargue de retorno
    ///   (inclusive) o la fecha del envío (estricta); null = lote sin enviar (sin retorno ni impacto).</item>
    /// <item><c>@PerfilesContacto</c>: JSON con las claves normalizadas de <see cref="PerfilesContacto"/>.</item>
    /// <item><c>@PaginaEnvioID</c>, <c>@Offset</c>, <c>@Tam</c>: página del detalle (solo si <paramref name="conPagina"/>).</item>
    /// </list>
    ///
    /// <para>Por titular (<c>#M</c>):</para>
    /// <list type="bullet">
    /// <item><b>Buscado</b>: su ÚLTIMA fila en la fuente del lote (mismo desempate que la etapa 1: fecha DESC y, en
    ///   empate, la que cumple el predicado) tiene fecha posterior al umbral. Equivale a "tiene alguna fila desde
    ///   el umbral": la última es la de fecha máxima. La fecha se compara sin truncar (RUES y Claro guardan hora).</item>
    /// <item><b>Localizado</b>: buscado y esa última fila cumple <c>PredicadoEncontrado</c>.</item>
    /// <item><b>Gestionado</b>: <c>UltimoEstadoTitular.FechaUltimaGestion</c> (por titular y cartera del lote) posterior al
    ///   DÍA del envío (FechaUltimaGestion es date: lo del mismo día no se puede ubicar antes o después).</item>
    /// <item><b>Pasó a contacto</b>: gestionado, su UltimoPerfil actual es de contacto y el de la foto no lo era.</item>
    /// <item><b>Pagos</b>: <c>dbo.Pago</c> por <c>CedulaTitular</c> con <c>FechaPago</c> posterior al día del envío (cualquier cartera).</item>
    /// <item><b>Promesa vigente</b>: <c>FechaPromesa</c> actual ≥ hoy y distinta de la de la foto (nueva desde el envío).</item>
    /// </list>
    /// Result sets: (1) una fila por lote con los conteos (<see cref="MetricasCrudasEnvio"/>); (2) si
    /// <paramref name="conPagina"/>, la página del detalle de <c>@PaginaEnvioID</c> por prioridad.
    /// </summary>
    /// <param name="fuentes">Las fuentes de los lotes pedidos (se arma un bloque por fuente).</param>
    public static string Metricas(TablasEnvio t, IReadOnlyCollection<FuenteLocalizacion> fuentes, bool conPagina)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            SET NOCOUNT ON;
            DROP TABLE IF EXISTS #L, #D, #U, #P, #M;
            DECLARE @Hoy date = CAST(SYSDATETIME() AS date);

            -- ── Lotes pedidos y su umbral de retorno (lo elige C#) ──────────────
            CREATE TABLE #L (EnvioID int NOT NULL PRIMARY KEY, Desde datetime2(3) NULL, Inclusivo bit NOT NULL);
            INSERT INTO #L (EnvioID, Desde, Inclusivo)
            SELECT e, d, ISNULL(i, 0) FROM OPENJSON(@Lotes) WITH (e int '$.e', d datetime2(3) '$.d', i bit '$.i');

            -- ── Detalle de esos lotes con la foto del envío ─────────────────────
            SELECT d.EnvioID, d.TitularID, e.Fuente, e.CarteraID, CAST(e.FechaEnvio AS date) AS DiaEnvio,
                   l.Desde, l.Inclusivo, d.UltimoPerfil AS PerfilFoto, d.FechaPromesa AS PromesaFoto
              INTO #D
              FROM #L l
              INNER JOIN {t.Envio} e   ON e.EnvioID = l.EnvioID
              INNER JOIN {t.Detalle} d ON d.EnvioID = l.EnvioID
            OPTION (MAXDOP 4);
            CREATE CLUSTERED INDEX IX_D ON #D (TitularID, EnvioID);

            -- ── Última fila de cada titular en la fuente de su lote ─────────────
            CREATE TABLE #U (EnvioID int NOT NULL, TitularID bigint NOT NULL, Enc tinyint NOT NULL, Fecha datetime2(3) NULL);

            """);

        foreach (var f in fuentes.DistinctBy(x => x.Clave))
            sb.Append(UltimaFilaFuente(f));

        sb.Append($"""
            CREATE CLUSTERED INDEX IX_U ON #U (EnvioID, TitularID);

            -- ── Pagos posteriores al día del envío (Pago no tiene índice por cédula: HASH JOIN) ─
            SELECT d.EnvioID, d.TitularID, COUNT(*) AS Pagos,
                   SUM(p.ValorPagado) AS Valor, SUM(p.Honorarios) AS Honorarios
              INTO #P
              FROM #D d
              INNER JOIN dbo.Pago p ON p.CedulaTitular = d.TitularID AND p.FechaPago > d.DiaEnvio
             GROUP BY d.EnvioID, d.TitularID
            OPTION (MAXDOP 4, HASH JOIN);
            CREATE CLUSTERED INDEX IX_P ON #P (EnvioID, TitularID);

            -- ── Perfiles con contacto (clasificados en C#, PerfilesContacto) ────
            DECLARE @PC TABLE (Perfil varchar(60) COLLATE Latin1_General_CI_AI NOT NULL PRIMARY KEY);
            INSERT INTO @PC (Perfil)
            SELECT DISTINCT CAST(value AS varchar(60)) FROM OPENJSON(@PerfilesContacto);

            -- ── Por titular ─────────────────────────────────────────────────────
            -- UltimoEstadoTitular por su PK (TitularID, CarteraID): LOOP JOIN + FORCESEEK, como las sugerencias.
            SELECT d.EnvioID, d.TitularID,
                   b.Buscado,
                   CAST(CASE WHEN b.Buscado = 1 AND u.Enc = 1 THEN 1 ELSE 0 END AS tinyint)            AS Localizado,
                   u.Fecha                                                                           AS FechaDato,
                   g.Gestionado,
                   CAST(CASE WHEN g.Gestionado = 1 AND c.Ahora = 1 AND c.Foto = 0 THEN 1 ELSE 0 END AS tinyint) AS PasoAContacto,
                   ISNULL(p.Pagos, 0)                                                                AS Pagos,
                   CAST(ISNULL(p.Valor, 0) AS decimal(19,2))                                         AS ValorPagado,
                   CAST(ISNULL(p.Honorarios, 0) AS decimal(19,2))                                    AS Honorarios,
                   CAST(CASE WHEN d.DiaEnvio IS NOT NULL AND a.FechaPromesa >= @Hoy
                              AND (d.PromesaFoto IS NULL OR a.FechaPromesa <> d.PromesaFoto) THEN 1 ELSE 0 END AS tinyint) AS PromesaNueva,
                   a.UltimoPerfil AS PerfilActual, a.FechaUltimaGestion AS GestionActual, a.FechaPromesa AS PromesaActual
              INTO #M
              FROM #D d
              LEFT JOIN #U u ON u.EnvioID = d.EnvioID AND u.TitularID = d.TitularID
              LEFT JOIN #P p ON p.EnvioID = d.EnvioID AND p.TitularID = d.TitularID
              LEFT JOIN dbo.UltimoEstadoTitular a WITH (INDEX(1), FORCESEEK)
                     ON a.TitularID = d.TitularID AND a.CarteraID = d.CarteraID
             CROSS APPLY (SELECT {GeneradorSqlSugerencias.NormalizarPerfilSql("a.UltimoPerfil")} AS Ahora,
                                 {GeneradorSqlSugerencias.NormalizarPerfilSql("d.PerfilFoto")} AS Foto) n
              LEFT JOIN @PC pa ON pa.Perfil = n.Ahora
              LEFT JOIN @PC pf ON pf.Perfil = n.Foto
             CROSS APPLY (SELECT CAST(CASE WHEN d.Desde IS NOT NULL AND u.Fecha IS NOT NULL
                                            AND (u.Fecha > d.Desde OR (d.Inclusivo = 1 AND u.Fecha = d.Desde))
                                           THEN 1 ELSE 0 END AS tinyint) AS Buscado) b
             CROSS APPLY (SELECT CAST(CASE WHEN d.DiaEnvio IS NOT NULL AND a.FechaUltimaGestion > d.DiaEnvio
                                           THEN 1 ELSE 0 END AS tinyint) AS Gestionado) g
             CROSS APPLY (SELECT CASE WHEN pa.Perfil IS NOT NULL {Tercero("n.Ahora")} THEN 1 ELSE 0 END AS Ahora,
                                 CASE WHEN pf.Perfil IS NOT NULL {Tercero("n.Foto")} THEN 1 ELSE 0 END AS Foto) c
            OPTION (MAXDOP 4, LOOP JOIN, FORCE ORDER);

            -- ── 1. Por lote ─────────────────────────────────────────────────────
            SELECT EnvioID,
                   COUNT(*)                                             AS Titulares,
                   SUM(CAST(Buscado AS int))                            AS Buscados,
                   SUM(CAST(Localizado AS int))                         AS Localizados,
                   MIN(CASE WHEN Buscado = 1 THEN FechaDato END)        AS PrimerDato,
                   SUM(CAST(Gestionado AS int))                         AS Gestionados,
                   SUM(CAST(PasoAContacto AS int))                      AS PasaronAContacto,
                   SUM(CASE WHEN Pagos > 0 THEN 1 ELSE 0 END)           AS TitularesConPago,
                   SUM(Pagos)                                           AS Pagos,
                   CAST(SUM(ValorPagado) AS decimal(19,2))              AS ValorPagado,
                   CAST(SUM(Honorarios) AS decimal(19,2))               AS Honorarios,
                   SUM(CAST(PromesaNueva AS int))                       AS PromesasVigentes
              FROM #M
             GROUP BY EnvioID
            OPTION (MAXDOP 4);

            """);

        if (conPagina)
            sb.Append($"""
                -- ── 2. Página del detalle ───────────────────────────────────────────
                SELECT m.TitularID, ti.Nombre, dt.Categoria, dt.Prioridad, dt.UltimaConsultaPrevia,
                       dt.SaldoTotal, dt.UltimoPerfil, dt.FechaUltimaGestion,
                       m.Buscado, m.Localizado, m.FechaDato, m.PerfilActual, m.GestionActual,
                       m.Gestionado, m.PasoAContacto, m.ValorPagado,
                       CASE WHEN m.PromesaActual >= @Hoy THEN m.PromesaActual END AS PromesaVigente
                  FROM #M m
                  INNER JOIN {t.Detalle} dt ON dt.EnvioID = m.EnvioID AND dt.TitularID = m.TitularID
                  LEFT JOIN dbo.Titular ti  ON ti.NumeroDocumento = m.TitularID
                 WHERE m.EnvioID = @PaginaEnvioID
                 ORDER BY dt.Prioridad DESC, m.TitularID
                OFFSET @Offset ROWS FETCH NEXT @Tam ROWS ONLY;

                """);

        sb.Append("DROP TABLE IF EXISTS #L, #D, #U, #P, #M;\n");
        return sb.ToString();
    }

    /// <summary>
    /// Bloque de <see cref="Metricas"/> para una fuente: la última fila de cada titular de <c>#D</c> cuyo lote es
    /// de esa fuente, con <c>Enc</c> (predicado del catálogo) y la fecha SIN truncar.
    /// </summary>
    public static string UltimaFilaFuente(FuenteLocalizacion f)
    {
        if (!TablaDbo.IsMatch(f.Tabla))
            throw new InvalidOperationException($"Tabla no permitida en el catálogo: '{f.Tabla}'.");
        var fecha = "s." + Validar(f.ColumnaFecha);
        var cruce = f.TitularIdComoTexto ? "TRY_CAST(s.TitularID AS bigint)" : "s.TitularID";

        return $"""
            -- {Validar(f.Clave)}
            INSERT INTO #U (EnvioID, TitularID, Enc, Fecha)
            SELECT x.EnvioID, x.TitularID, x.Enc, x.Fecha
              FROM (
                    SELECT d.EnvioID, d.TitularID, e.Enc,
                           CAST({fecha} AS datetime2(3)) AS Fecha,
                           ROW_NUMBER() OVER (PARTITION BY d.EnvioID, d.TitularID
                                              ORDER BY {fecha} DESC, e.Enc DESC) AS rn
                      FROM #D d
                      INNER JOIN {f.Tabla} s ON {cruce} = d.TitularID
                      CROSS APPLY (SELECT CAST(CASE WHEN {f.PredicadoEncontrado} THEN 1 ELSE 0 END AS tinyint) AS Enc) e
                     WHERE d.Fuente = '{Validar(f.Clave)}'
                   ) x
             WHERE x.rn = 1
            OPTION (MAXDOP 4);

            """;
    }

    /// <summary>OR sobre las palabras de tercero (<see cref="PerfilesContacto.PalabrasTercero"/>) de un perfil normalizado.</summary>
    private static string Tercero(string columna) =>
        string.Concat(PerfilesContacto.PalabrasTercero.Select(p => $"OR {columna} LIKE '%{Validar(p)}%' "));

    private static string Validar(string identificador) =>
        Identificador.IsMatch(identificador)
            ? identificador
            : throw new InvalidOperationException($"Identificador SQL no permitido: '{identificador}'.");
}

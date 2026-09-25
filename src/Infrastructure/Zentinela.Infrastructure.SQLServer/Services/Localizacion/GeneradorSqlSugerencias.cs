using System.Globalization;
using System.Text;
using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// SQL de las sugerencias de localización (ADR-040, etapa 2): UN batch con parámetros que arma, sobre los
/// titulares del par, una fila por titular con máscaras de bits por fuente, y devuelve tres result sets
/// que <see cref="DespliegueSugerencias"/> convierte en KPIs y en el top.
///
/// <list type="number">
/// <item><c>#Tit</c> = titulares del par (<c>Titular_Cartera</c> con <c>Lider_Cartera.Activo = 1</c> y
///   titular en <c>dbo.Titular</c>, igual que el Total del Calificador y de la etapa 1) con su foto de
///   <c>UltimoEstadoTitular</c> (por TitularID + CarteraID), copiada a la variable de tabla <c>@Tit</c>.
///   Cartera completa (<c>@LiderID</c> null): un titular con dos líderes cuenta una vez, con el menor LiderID.</item>
/// <item><c>@PG</c>: la clasificación de perfiles que hace C# (<c>PerfilesSugerencia</c>) sobre las claves
///   normalizadas; el perfil de <c>UltimoEstadoTitular</c> se normaliza igual en SQL (mayúsculas, espacios
///   colapsados) y se compara sin tildes (<c>Latin1_General_CI_AI</c>).</item>
/// <item><c>@EnvM</c>: fuentes a las que el titular ya fue enviado (lote ENVIADO o CERRADO, de cualquier
///   par) dentro de la ventana, como máscara. Solo si existen las tablas de envíos.</item>
/// <item><c>#TE</c>: la última fila de CADA fuente Funcional (<see cref="GeneradorSqlLocalizacion.UltimaFilaConsulta"/>:
///   mismo predicado y mismo desempate Fecha, Enc, Con que la etapa 1) agregada por titular en máscaras
///   (buscado, encontrado, no encontrado y viejo, encontrado pero viejo), contacto fresco, exclusión del
///   titular (perfil o promesa), falta de saldo y los puntos que no dependen de la fuente, más la última
///   consulta de cada fuente pedida (<c>U{i}</c>).</item>
/// </list>
///
/// Result sets: (1) titulares, con saldo, P90 del saldo y si hay registro de envíos; (2) las combinaciones
/// de máscaras con su cantidad de titulares, contacto fresco y saldo (pocas miles: C# las despliega por
/// fuente para los conteos y los titulares distintos por motivo); (3) los @Top
/// mejores titulares por su mejor fila, con todo lo necesario para desplegar sus filas en C#.
///
/// <para><b>Rendimiento</b> (par de 223k titulares, 16 fuentes, medido 2026-09-25):</para>
/// <list type="bullet">
/// <item><c>UltimoEstadoTitular</c> pesa ~600 MB (la columna Gestion es nvarchar(1000)): recorrerla con
///   HASH JOIN cuesta ~2 s; buscar por su PK desde el par materializado con LOOP JOIN, ~0,5 s.</item>
/// <item>Las fuentes (18 M de filas) se leen con HASH JOIN (el optimizador arma nested loops O(n²) sobre
///   estos heaps si se le deja; un LOOP JOIN por el índice (TitularID, Fecha) resultó el doble de lento) en
///   UNA sentencia sin tablas intermedias: escribir en tempdb es lo caro en este servidor (materializar
///   los 2,7 M candidatos costaba 7 s). Es el piso: ~2,5 s a MAXDOP 4.</item>
/// <item>Compilar: en un batch ad hoc, una sentencia que lee una # creada en el mismo batch se recompila
///   en CADA ejecución (la # es un objeto nuevo); la de <c>#TE</c> tardaba 1,3 s en compilar. Por eso lee
///   variables de tabla (<c>@G</c>, <c>@Tit</c>, <c>@PG</c>, <c>@EnvM</c>), cuyo plan sí se reutiliza, y nada lleva
///   RECOMPILE salvo el llenado de <c>@G</c> (depende de si hay líder y compila en milisegundos).</item>
/// </list>
/// </summary>
internal static class GeneradorSqlSugerencias
{
    /// <summary>Fuentes Funcionales del panel: sobre ellas se calcula "nunca buscado en ninguna Funcional",
    /// "contacto fresco" y "encontrado en". Su posición en esta lista es su bit en las máscaras.</summary>
    public static IReadOnlyList<FuenteLocalizacion> Funcionales { get; } =
        GeneradorSqlLocalizacion.Fuentes.Where(f => f.Estado == EstadoFuente.Funcional).ToList();

    /// <summary>Bit de una fuente Funcional en las máscaras.</summary>
    public static long Bit(int indice) => 1L << indice;

    /// <summary>Tramo de tamaño del par para separar planes: hasta 20k, 100k, 400k filas, o más.</summary>
    public static string TramoTamano(int filasPar) => filasPar switch
    {
        < 20_000  => "chico",
        < 100_000 => "mediano",
        < 400_000 => "grande",
        _         => "enorme",
    };

    /// <summary>Índices de las fuentes pedidas en <see cref="Funcionales"/>, en el orden del catálogo (el de
    /// las columnas <c>U{i}</c> del result set 3).</summary>
    public static IReadOnlyList<int> IndicesPedidas(IReadOnlyList<FuenteLocalizacion> fuentes) =>
        Enumerable.Range(0, Funcionales.Count).Where(i => fuentes.Contains(Funcionales[i])).ToList();

    /// <summary>
    /// Normalización SQL de un perfil, la misma de <c>PerfilesSugerencia.Normalizar</c> salvo las tildes,
    /// que se ignoran al comparar (<c>COLLATE Latin1_General_CI_AI</c>): tab y espacio duro a espacio,
    /// espacios repetidos colapsados, recorte y mayúsculas. NULL → ''.
    /// </summary>
    public static string NormalizarPerfilSql(string columna) =>
        $"UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL({columna}, ''), CHAR(9), ' '), CHAR(160), ' '), '    ', ' '), '  ', ' '), '  ', ' ')))) COLLATE Latin1_General_CI_AI";

    /// <summary>
    /// El batch. Parámetros: <c>@CarteraID</c>, <c>@LiderID</c> (null = cartera completa), <c>@Perfiles</c>
    /// (JSON <c>[{"p":…,"m":…,"pts":…,"yp":…}]</c> con claves normalizadas), <c>@Hoy</c>,
    /// <c>@LimUmbral</c>, <c>@LimRefresco</c> (null = sin "encontrado pero viejo"), <c>@LimFresco</c>,
    /// <c>@LimEnvio</c> (null = no excluir enviados), <c>@Lim30</c>, <c>@Lim90</c>, <c>@SoloConSaldo</c>,
    /// <c>@IncluirLocalizados</c>, <c>@Top</c> y los pesos <c>@PtsSaldoMax</c>, <c>@PtsGestion90</c>,
    /// <c>@PtsGestion30</c>, <c>@PtsNuncaFuncional</c>, <c>@PtsNuncaFuente</c>, <c>@PtsNoEncViejo</c>,
    /// <c>@PenContacto</c>.
    /// </summary>
    /// <param name="fuentes">Fuentes pedidas (todas Funcionales).</param>
    /// <param name="filasPar">Filas de Titular_Cartera del par (aproximado). Solo decide el tramo de tamaño que va
    /// como comentario en el texto: así un par chico y uno grande no comparten plan (el plan de las variables de
    /// tabla se arma con la cantidad de filas de la primera ejecución; medido: el par grande con el plan del chico,
    /// 10,7 s en vez de 4,5 s).</param>
    public static string Batch(IReadOnlyList<FuenteLocalizacion> fuentes, int filasPar = 0)
    {
        if (fuentes.Count == 0) throw new ArgumentException("Se necesita al menos una fuente.", nameof(fuentes));
        foreach (var f in fuentes)
            if (!Funcionales.Contains(f))
                throw new ArgumentException($"La fuente {f.Clave} no es Funcional.", nameof(fuentes));

        var n = Funcionales.Count;
        var pedidas = IndicesPedidas(fuentes);
        var mascaraPedidas = pedidas.Aggregate(0L, (m, i) => m | Bit(i));
        string I(int i) => i.ToString(CultureInfo.InvariantCulture);
        string B(int i) => $"CAST({Bit(i)} AS bigint)";

        // La última fila de cada fuente Funcional, como tablas derivadas de una sola sentencia.
        var ultimas = string.Join("\n              UNION ALL\n", Enumerable.Range(0, n).Select(i => $"""
                    SELECT f.TitularID, f.Enc, f.Con, f.Fecha, {B(i)} AS Bit
                      FROM (
            {GeneradorSqlLocalizacion.UltimaFilaConsulta(Funcionales[i], titulares: "@Tit")}
                           ) f
            """));
        var fechas = string.Join(",\n", pedidas.Select(i => $"                       MAX(CASE WHEN u.Bit = {Bit(i)} THEN u.Fecha END) AS U{I(i)}"));
        var columnasU = string.Join(", ", pedidas.Select(i => $"x.U{I(i)}"));
        var columnasUte = string.Join(", ", pedidas.Select(i => $"U{I(i)}"));
        var casosEnvio = string.Join(" ", Enumerable.Range(0, n).Select(i => $"WHEN '{Funcionales[i].Clave}' THEN {Bit(i)}"));

        var sb = new StringBuilder();
        sb.Append($"""
            -- tamaño: {TramoTamano(filasPar)}
            SET NOCOUNT ON;
            DROP TABLE IF EXISTS #Tit, #TE;
            DECLARE @Pedidas bigint = {mascaraPedidas};

            -- ── Titulares del par ───────────────────────────────────────────────
            -- En pasos a propósito: con el par ya materializado, LOOP JOIN busca por PK en Titular y en
            -- UltimoEstadoTitular (~0,5 s en 223k titulares, contra ~2 s de recorrerla con HASH JOIN; con las
            -- sugerencias de join en una sola sentencia el plan salió de 86 s). Saldo NULL = sin saldo.
            -- @G y @Tit son variables de tabla: las sentencias que leen una # creada en el mismo batch se
            -- recompilan en cada ejecución y crean estadísticas automáticas; con variables de tabla no.
            DECLARE @G TABLE (TitularID bigint NOT NULL PRIMARY KEY, LiderID int NOT NULL);
            INSERT INTO @G (TitularID, LiderID)
            SELECT tc.TitularID, MIN(tc.LiderID)
              FROM dbo.Titular_Cartera tc
              INNER JOIN dbo.Lider_Cartera lc
                      ON lc.LiderID = tc.LiderID AND lc.CarteraID = tc.CarteraID AND lc.Activo = 1
             WHERE tc.CarteraID = @CarteraID
               AND (@LiderID IS NULL OR tc.LiderID = @LiderID)
             GROUP BY tc.TitularID
            OPTION (RECOMPILE, MAXDOP 4);

            SELECT g.TitularID, g.LiderID, ti.Nombre,
                   CAST(CASE WHEN u.SaldoTotal > 0 THEN u.SaldoTotal END AS decimal(19,2)) AS Saldo,
                   u.UltimoPerfil, u.MejorPerfil, u.FechaUltimaGestion, u.FechaPromesa
              INTO #Tit
              FROM @G g
              INNER JOIN dbo.Titular ti WITH (FORCESEEK) ON ti.NumeroDocumento = g.TitularID
              LEFT JOIN dbo.UltimoEstadoTitular u WITH (INDEX(1), FORCESEEK)
                     ON u.TitularID = g.TitularID AND u.CarteraID = @CarteraID
            -- FORCE ORDER + INDEX(1)/FORCESEEK: @G afuera y búsquedas por la PK (TitularID, CarteraID) adentro. Sin
            -- ellos, con las estadísticas de IX_UltimoEstado_CarteraID viejas (el ETL recarga la tabla cada día), el
            -- optimizador buscó por CarteraID, guardó esas filas en un spool y las recorrió por cada titular (O(n²)):
            -- medido 63 s en 36k titulares.
            OPTION (MAXDOP 4, LOOP JOIN, FORCE ORDER);

            DECLARE @Tit TABLE (TitularID bigint NOT NULL PRIMARY KEY, LiderID int NOT NULL, Nombre varchar(70) NULL,
                                Saldo decimal(19,2) NULL, UltimoPerfil varchar(50) NULL, MejorPerfil varchar(50) NULL,
                                FechaUltimaGestion date NULL, FechaPromesa date NULL);
            INSERT INTO @Tit (TitularID, LiderID, Nombre, Saldo, UltimoPerfil, MejorPerfil, FechaUltimaGestion, FechaPromesa)
            SELECT TitularID, LiderID, Nombre, Saldo, UltimoPerfil, MejorPerfil, FechaUltimaGestion, FechaPromesa FROM #Tit;

            -- P90 del saldo solo entre los que tienen saldo: con los ceros, el P90 de una cartera con poco
            -- saldo sería 0 y nadie sumaría puntos por saldo.
            DECLARE @Titulares int, @ConSaldo int, @P90 decimal(19,2), @ConEnvios bit;
            SELECT @Titulares = COUNT(*), @ConSaldo = ISNULL(SUM(CASE WHEN Saldo > 0 THEN 1 ELSE 0 END), 0) FROM @Tit;
            SELECT TOP (1) @P90 = CAST(PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY Saldo) OVER () AS decimal(19,2))
              FROM @Tit WHERE Saldo > 0;

            SET @ConEnvios = CASE WHEN OBJECT_ID('dbo.LocalizacionEnvio', 'U') IS NOT NULL
                                   AND OBJECT_ID('dbo.LocalizacionEnvioDetalle', 'U') IS NOT NULL THEN 1 ELSE 0 END;

            -- ── Perfiles (clasificados en C#) ───────────────────────────────────
            DECLARE @PG TABLE (Perfil varchar(60) COLLATE Latin1_General_CI_AI NOT NULL, Motivo varchar(20) COLLATE DATABASE_DEFAULT NULL,
                              Puntos int NOT NULL, YaPago bit NOT NULL);
            INSERT INTO @PG (Perfil, Motivo, Puntos, YaPago)
            SELECT p, m, pts, yp FROM OPENJSON(@Perfiles) WITH (p varchar(60) '$.p', m varchar(20) '$.m', pts int '$.pts', yp bit '$.yp');

            -- ── Enviados recientes (si ya existen las tablas de la etapa 3) ─────
            DECLARE @EnvM TABLE (TitularID bigint NOT NULL PRIMARY KEY, EnvMask bigint NOT NULL);
            IF @ConEnvios = 1 AND @LimEnvio IS NOT NULL
                INSERT INTO @EnvM (TitularID, EnvMask)
                SELECT q.TitularID, SUM(q.Bit)
                  FROM (SELECT DISTINCT d.TitularID, CAST(CASE e.Fuente {casosEnvio} ELSE 0 END AS bigint) AS Bit
                          FROM dbo.LocalizacionEnvio e
                          INNER JOIN dbo.LocalizacionEnvioDetalle d ON d.EnvioID = e.EnvioID
                          INNER JOIN @Tit t ON t.TitularID = d.TitularID
                         WHERE e.Estado IN ('ENVIADO', 'CERRADO')
                           AND e.FechaEnvio >= @LimEnvio) q
                 GROUP BY q.TitularID
                OPTION (MAXDOP 4, HASH JOIN);

            -- ── Una fila por titular con candidatos ─────────────────────────────
            -- Categorías por fuente pedida: Cat1 = nunca buscado, Cat2 = no encontrado y viejo, Cat3 =
            -- encontrado pero viejo (solo con @LimRefresco). Un buscado sin fecha cuenta como viejo (no se sabe
            -- cuándo se consultó), como en la etapa 1. Puntos = PrioridadSugerencia.Calcular.
            -- Las consultas finales leen #TE sin WHERE, JOIN ni GROUP BY sobre varias columnas: cada columna
            -- filtrada o agrupada de una # nueva dispara una estadística automática (medido: 8 estadísticas,
            -- ~1 s). Por eso #TE trae ya los nombres, una "Firma" para agrupar y "OrdenTop" para el top.
            SELECT t.TitularID, t.Saldo, m.MotivoTitular, m.SinSaldo,
                   ISNULL(em.EnvMask, 0) AS EnvMask,
                   ISNULL(x.EncMask, 0) AS EncMask,
                   k.Cat1, k.Cat2, k.Cat3,
                   CAST(CASE WHEN x.TitularID IS NULL THEN 0 ELSE 1 END AS bit) AS BuscadoFuncional,
                   CAST(ISNULL(x.ConFresco, 0) AS bit) AS ConFresco,
                   p.PtsPerfil, p.PtsSaldo, p.PtsGestion, p.PtsNuncaFuncional, p.PtsContacto,
                   p.PtsPerfil + p.PtsSaldo + p.PtsGestion + p.PtsNuncaFuncional + p.PtsContacto AS PtsBase,
                   -- Mejor fila del titular entre sus fuentes no enviadas, si no tiene exclusión propia (null =
                   -- no le queda ninguna): ordena el top.
                   CASE WHEN m.MotivoTitular IS NULL AND m.SinSaldo = 0
                        THEN p.PtsPerfil + p.PtsSaldo + p.PtsGestion + p.PtsNuncaFuncional + p.PtsContacto
                             + GREATEST(CASE WHEN k.Cat1 & ~ISNULL(em.EnvMask, 0) <> 0 THEN @PtsNuncaFuente END,
                                        CASE WHEN k.Cat2 & ~ISNULL(em.EnvMask, 0) <> 0 THEN @PtsNoEncViejo END,
                                        CASE WHEN k.Cat3 & ~ISNULL(em.EnvMask, 0) <> 0 THEN 0 END)
                   END AS OrdenTop,
                   CONCAT(m.MotivoTitular, '|', CAST(m.SinSaldo AS tinyint), '|', ISNULL(em.EnvMask, 0), '|', k.Cat1, '|', k.Cat2, '|', k.Cat3) AS Firma,
                   t.Nombre, t.LiderID, t.UltimoPerfil, t.FechaUltimaGestion,
                   {columnasU}
              INTO #TE
              FROM @Tit t
              LEFT JOIN @PG pu   ON pu.Perfil = {NormalizarPerfilSql("t.UltimoPerfil")}
              LEFT JOIN @PG pm   ON pm.Perfil = {NormalizarPerfilSql("t.MejorPerfil")}
              LEFT JOIN @EnvM em ON em.TitularID = t.TitularID
              LEFT JOIN (
                    SELECT u.TitularID,
                           SUM(u.Bit) AS BuscadoMask,
                           SUM(CASE WHEN u.Enc = 1 THEN u.Bit ELSE 0 END) AS EncMask,
                           SUM(CASE WHEN u.Enc = 0 AND (u.Fecha IS NULL OR u.Fecha < @LimUmbral) THEN u.Bit ELSE 0 END) AS NoEncViejoMask,
                           SUM(CASE WHEN u.Enc = 1 AND @LimRefresco IS NOT NULL AND (u.Fecha IS NULL OR u.Fecha < @LimRefresco) THEN u.Bit ELSE 0 END) AS EncViejoMask,
                           MAX(CASE WHEN u.Con = 1 AND u.Fecha >= @LimFresco THEN 1 ELSE 0 END) AS ConFresco,
            {fechas}
                      FROM (
            {ultimas}
                           ) u
                     GROUP BY u.TitularID
                   ) x ON x.TitularID = t.TitularID
             CROSS APPLY (SELECT @Pedidas & ~ISNULL(x.BuscadoMask, 0)   AS Cat1,
                                 @Pedidas & ISNULL(x.NoEncViejoMask, 0) AS Cat2,
                                 @Pedidas & ISNULL(x.EncViejoMask, 0)   AS Cat3) k
             CROSS APPLY (SELECT ISNULL(pu.Puntos, 0) AS PtsPerfil,
                                 CASE WHEN @P90 > 0 AND t.Saldo > 0
                                      THEN CAST(ROUND(@PtsSaldoMax * CASE WHEN t.Saldo >= @P90 THEN CAST(1 AS decimal(38,20))
                                                                          ELSE CAST(t.Saldo AS decimal(38,20)) / @P90 END, 0) AS int)
                                      ELSE 0 END AS PtsSaldo,
                                 CASE WHEN t.FechaUltimaGestion IS NULL OR t.FechaUltimaGestion < @Lim90 THEN @PtsGestion90
                                      WHEN t.FechaUltimaGestion < @Lim30 THEN @PtsGestion30
                                      ELSE 0 END AS PtsGestion,
                                 CASE WHEN x.TitularID IS NULL THEN @PtsNuncaFuncional ELSE 0 END AS PtsNuncaFuncional,
                                 CASE WHEN x.ConFresco = 1 AND @IncluirLocalizados = 0 THEN -@PenContacto ELSE 0 END AS PtsContacto) p
             CROSS APPLY (SELECT CASE WHEN pm.YaPago = 1          THEN 'YA_PAGO'
                                      WHEN pu.Motivo IS NOT NULL  THEN pu.Motivo
                                      WHEN t.FechaPromesa >= @Hoy THEN 'PROMESA'
                                 END AS MotivoTitular,
                                 CAST(CASE WHEN @SoloConSaldo = 1 AND t.Saldo IS NULL THEN 1 ELSE 0 END AS bit) AS SinSaldo) m
             WHERE (k.Cat1 | k.Cat2 | k.Cat3) <> 0
            OPTION (MAXDOP 4, HASH JOIN);

            -- ── Resultados ──────────────────────────────────────────────────────
            -- 1. Universo.
            SELECT @Titulares AS Titulares, @ConSaldo AS ConSaldo, @P90 AS P90, @ConEnvios AS ConEnvios;

            -- 2. Combinaciones de máscaras (C# las despliega por fuente para los conteos y cuenta de ahí los
            --    titulares distintos por motivo: todos los titulares de una combinación tienen los mismos datos).
            SELECT MIN(MotivoTitular) AS MotivoTitular, CAST(MIN(CAST(SinSaldo AS tinyint)) AS bit) AS SinSaldo,
                   MIN(EnvMask) AS EnvMask, MIN(Cat1) AS Cat1, MIN(Cat2) AS Cat2, MIN(Cat3) AS Cat3,
                   COUNT(*) AS N, SUM(CAST(ConFresco AS int)) AS NConFresco,
                   CAST(SUM(ISNULL(Saldo, 0)) AS decimal(19,2)) AS Saldo
              FROM #TE
             GROUP BY Firma
            OPTION (MAXDOP 4);

            -- 3. Los @Top mejores titulares por su mejor fila (contienen a las @Top mejores filas). Sin WHERE:
            --    en orden descendente los OrdenTop NULL (excluidos) quedan al final y C# los descarta.
            SELECT TOP (@Top)
                   TitularID, Nombre, LiderID, Saldo, UltimoPerfil, FechaUltimaGestion,
                   PtsPerfil, PtsSaldo, PtsGestion, PtsNuncaFuncional, PtsContacto, PtsBase,
                   EncMask, BuscadoFuncional, ConFresco, EnvMask, Cat1, Cat2, Cat3,
                   {columnasUte}, OrdenTop
              FROM #TE
             ORDER BY OrdenTop DESC, ISNULL(Saldo, 0) DESC, TitularID
            OPTION (MAXDOP 4);

            DROP TABLE IF EXISTS #Tit, #TE;

            """);
        return sb.ToString();
    }
}

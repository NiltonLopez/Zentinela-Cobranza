using System.Text;
using System.Text.RegularExpressions;
using Zentinela.Application;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Arma el batch del precálculo de los Informes de Localización (ADR-040) a partir de
/// <see cref="CatalogoFuentesLocalizacion"/>, igual que <c>GeneradorSqlCargue</c> arma el del cargue:
/// agregar una fuente al catálogo la suma al precálculo sin tocar SQL a mano.
///
/// <para><b>Qué calcula</b> (parámetros <c>@CorteID int</c>, <c>@Hoy date</c> y, desde la etapa 4,
/// <c>@PerfilesContacto nvarchar(max)</c> —JSON con las claves de <c>PerfilesContacto</c>— y <c>@DiasGestion int</c>):</para>
/// <list type="number">
/// <item><c>#Alc</c>: filas de <c>Titular_Cartera</c> de los pares activos (<c>Lider_Cartera.Activo = 1</c>
///   y <c>Cartera.Activa = 1</c>) cuyo titular existe en <c>dbo.Titular</c> (la BaseMadre del Calificador
///   hace INNER JOIN a Titular: sin ese filtro el Total no daría igual). <c>Interna</c> = relación no
///   externa y ni el líder ni la cartera son "BASE GENERAL" (lo que los rankings excluyen por defecto).</item>
/// <item><c>#Tit</c>: titulares distintos de <c>#Alc</c>.</item>
/// <item><c>#F_&lt;Clave&gt;</c> por cada fuente del panel: la última fila de cada titular de <c>#Tit</c>
///   con <c>Enc</c> (predicado "encontrado"), <c>Con</c> (celular o email) y <c>Fecha</c>.</item>
/// <item>Agregados por par y fuente (<c>#RF</c>), por líder y fuente con titulares distintos
///   (<c>#RFL</c>, LiderID 0 = todos los pares), métricas que cruzan fuentes por titular (<c>#X</c>) y
///   el saldo de <c>UltimoEstadoTitular</c> (por TitularID + CarteraID): <c>#RP</c> por par y <c>#RL</c>
///   por líder (titulares distintos; SoloInternas 0/1).</item>
/// <item><c>#RA</c> (etapa 4): por par y asesor (<c>UltimoEstadoTitular.AgenteID</c>, el último gestor): titulares,
///   con dato de contacto, con saldo, sin contacto con saldo, gestionados en los últimos <c>@DiasGestion</c> días y
///   con contacto efectivo (UltimoPerfil de contacto).</item>
/// <item>Una transacción corta copia los <c>#R*</c> a las tablas destino (la de asesores, solo si existe) y
///   devuelve Pares y Titulares.</item>
/// </list>
///
/// <para><b>Desempate de la "última fila".</b> El Calificador ordena solo por la fecha
/// (<c>ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC)</c> en
/// <c>fn_BaseMadreParaTitulares</c>): si un titular tiene dos filas con la misma fecha (la fecha es DATE
/// en 20 de las 21 fuentes), SQL Server elige cualquiera y el resultado puede cambiar entre corridas.
/// Aquí el orden es <c>Fecha DESC, Enc DESC, Con DESC</c>: en empate de fecha gana la fila con dato, y el
/// resultado es determinista. Por eso la paridad con el Calificador puede diferir en unos pocos
/// titulares por par y fuente, siempre titulares con empate de fecha (medido y documentado en ADR-040).</para>
///
/// <para>Todo con <c>OPTION (MAXDOP 4, HASH JOIN)</c>: servidor compartido de 8 núcleos, y el optimizador
/// arma nested loops O(n²) sobre estas tablas heap si se le deja (historia en CalificadorCarteraService).
/// Las #temp no llevan constraints con nombre (en tempdb el nombre de un constraint es global).</para>
/// </summary>
internal static class GeneradorSqlLocalizacion
{
    /// <summary>Esquema de las tablas destino en producción.</summary>
    public const string PrefijoProduccion = "dbo.";

    // Solo identificadores simples: todo sale del catálogo (código), pero se valida igual.
    private static readonly Regex Identificador = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex TablaDbo = new("^dbo\\.[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex Prefijo = new("^(dbo\\.|#{1,2})$", RegexOptions.Compiled);

    /// <summary>Fuentes que entran al precálculo: las 21 del panel del Calificador. CamaraEmpresa queda
    /// fuera: su clave es el NIT de la empresa, no el titular.</summary>
    public static IReadOnlyList<FuenteLocalizacion> Fuentes { get; } =
        CatalogoFuentesLocalizacion.DelPanel.ToList();

    /// <summary>Nombres de las tablas destino para un prefijo: <c>dbo.</c> en producción, <c>#</c> para
    /// correr el mismo batch contra tablas temporales (pruebas de paridad y rendimiento).</summary>
    public sealed record TablasDestino(string Fuente, string FuenteLider, string Par, string Lider, string Asesor)
    {
        public static TablasDestino ConPrefijo(string prefijo)
        {
            if (!Prefijo.IsMatch(prefijo))
                throw new ArgumentException($"Prefijo de tablas no permitido: '{prefijo}'.", nameof(prefijo));
            return new(
                prefijo + "LocalizacionResumenFuente",
                prefijo + "LocalizacionResumenFuenteLider",
                prefijo + "LocalizacionResumenPar",
                prefijo + "LocalizacionResumenLider",
                prefijo + "LocalizacionResumenAsesor");
        }

        /// <summary>
        /// <c>OBJECT_ID(…)</c> de la tabla de asesores (etapa 4): es opcional, para que un script aplicado antes de la
        /// etapa 4 no tumbe el corte (sin ella, el corte se calcula igual y el ranking de asesores avisa).
        /// </summary>
        public string ExisteAsesor => Asesor.StartsWith('#')
            ? $"OBJECT_ID('tempdb..{Asesor}', 'U') IS NOT NULL"
            : $"OBJECT_ID('{Asesor}', 'U') IS NOT NULL";
    }

    /// <summary>Tabla temporal con la última fila por titular de la fuente.</summary>
    public static string TablaTemporal(FuenteLocalizacion f) => "#F_" + Validar(f.Clave);

    /// <summary>
    /// <c>SELECT … INTO #F_&lt;Clave&gt;</c>: la última fila de cada titular de <c>#Tit</c> en la fuente,
    /// con Enc / Con (tinyint 0/1) y Fecha (date). NuevaEps guarda el TitularID como texto: se cruza con
    /// <c>TRY_CAST(s.TitularID AS bigint)</c>, como hace la BaseMadre.
    /// </summary>
    public static string UltimaFila(FuenteLocalizacion f) => $"""
        -- {f.Clave}: última fila por titular (desempate: en la misma fecha gana la fila con dato)
        {UltimaFilaConsulta(f, $"INTO {TablaTemporal(f)}")}
        OPTION (MAXDOP 4, HASH JOIN);
        CREATE UNIQUE CLUSTERED INDEX IX_F ON {TablaTemporal(f)} (TitularID);

        """;

    /// <summary>
    /// La consulta de <see cref="UltimaFila"/> sin <c>OPTION</c> ni índice: <c>TitularID, Enc, Con, Fecha</c>
    /// de la última fila de cada titular de <c>#Tit</c> en la fuente. <paramref name="into"/> va entre el
    /// SELECT y el FROM (<c>"INTO #F_X"</c>, o vacío para usarla como tabla derivada). Las sugerencias
    /// (etapa 2) la usan así, sobre la variable de tabla <c>@Tit</c> (<paramref name="titulares"/>), para no escribir
    /// las #F en tempdb ni recompilar en cada ejecución.
    /// </summary>
    public static string UltimaFilaConsulta(FuenteLocalizacion f, string into = "", string titulares = "#Tit")
    {
        if (!TablaDbo.IsMatch(f.Tabla))
            throw new InvalidOperationException($"Tabla no permitida en el catálogo: '{f.Tabla}'.");
        var fecha = "s." + Validar(f.ColumnaFecha);
        if (titulares is not ("#Tit" or "@Tit"))
            throw new ArgumentException($"Tabla de titulares no permitida: '{titulares}'.", nameof(titulares));
        var cruce = f.TitularIdComoTexto ? "TRY_CAST(s.TitularID AS bigint)" : "s.TitularID";
        var con = f.PredicadoContacto is null ? "0" : $"CASE WHEN {f.PredicadoContacto} THEN 1 ELSE 0 END";

        return $"""
            SELECT x.TitularID, x.Enc, x.Con, x.Fecha
              {into}
              FROM (
                    SELECT t.TitularID,
                           e.Enc, e.Con,
                           CAST({fecha} AS date) AS Fecha,
                           ROW_NUMBER() OVER (PARTITION BY t.TitularID
                                              ORDER BY {fecha} DESC, e.Enc DESC, e.Con DESC) AS rn
                      FROM {f.Tabla} s
                      INNER JOIN {titulares} t ON t.TitularID = {cruce}
                      CROSS APPLY (SELECT CAST(CASE WHEN {f.PredicadoEncontrado} THEN 1 ELSE 0 END AS tinyint) AS Enc,
                                          CAST({con} AS tinyint) AS Con) e
                   ) x
             WHERE x.rn = 1
            """;
    }

    /// <summary>Agregado por par (<c>#Alc</c>) o por líder (<c>#LT</c>) de una fuente.</summary>
    public static string Agregado(FuenteLocalizacion f, bool porLider)
    {
        var t = TablaTemporal(f);
        var (destino, origen, claves) = porLider
            ? ("#RFL", "#LT", "a.LiderID")
            : ("#RF", "#Alc", "a.LiderID, a.CarteraID");
        var columnasClave = porLider ? "LiderID" : "LiderID, CarteraID";

        // Un buscado sin fecha cuenta como viejo: no se sabe cuándo se consultó.
        return $"""
            INSERT INTO {destino} ({columnasClave}, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax)
            SELECT {claves}, '{Validar(f.Clave)}',
                   COUNT(*),
                   COUNT(f.TitularID),
                   ISNULL(SUM(CAST(f.Enc AS int)), 0),
                   SUM(CASE WHEN f.TitularID IS NOT NULL AND (f.Fecha IS NULL OR f.Fecha < @Lim90)  THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.TitularID IS NOT NULL AND (f.Fecha IS NULL OR f.Fecha < @Lim180) THEN 1 ELSE 0 END),
                   SUM(CASE WHEN f.TitularID IS NOT NULL AND (f.Fecha IS NULL OR f.Fecha < @Lim365) THEN 1 ELSE 0 END),
                   MAX(f.Fecha)
              FROM {origen} a
              LEFT JOIN {t} f ON f.TitularID = a.TitularID
             GROUP BY {claves}
            OPTION (MAXDOP 4, HASH JOIN);

            """;
    }

    /// <summary>Batch completo del precálculo contra las tablas destino de <paramref name="prefijo"/>.</summary>
    public static string Precalculo(string prefijo = PrefijoProduccion)
    {
        var d = TablasDestino.ConPrefijo(prefijo);
        var fuentes = Fuentes;
        var sb = new StringBuilder();

        sb.Append("""
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Lim90  date = DATEADD(DAY,  -90, @Hoy);
            DECLARE @Lim180 date = DATEADD(DAY, -180, @Hoy);
            DECLARE @Lim365 date = DATEADD(DAY, -365, @Hoy);

            -- ── 1. Alcance: pares activos ───────────────────────────────────────
            DROP TABLE IF EXISTS #Alc;
            SELECT tc.TitularID, tc.LiderID, tc.CarteraID,
                   CAST(CASE WHEN lc.EsExterna = 0
                              AND UPPER(LTRIM(RTRIM(l.Nombre)))        <> 'BASE GENERAL'
                              AND UPPER(LTRIM(RTRIM(c.NombreCartera))) <> 'BASE GENERAL'
                             THEN 1 ELSE 0 END AS tinyint) AS Interna
              INTO #Alc
              FROM dbo.Titular_Cartera tc
              INNER JOIN dbo.Lider_Cartera lc ON lc.LiderID = tc.LiderID AND lc.CarteraID = tc.CarteraID AND lc.Activo = 1
              INNER JOIN dbo.Cartera c        ON c.ID = tc.CarteraID AND c.Activa = 1
              INNER JOIN dbo.Lider l          ON l.ID = tc.LiderID
             WHERE EXISTS (SELECT 1 FROM dbo.Titular ti WHERE ti.NumeroDocumento = tc.TitularID)
            OPTION (MAXDOP 4, HASH JOIN);

            DROP TABLE IF EXISTS #Tit;
            SELECT DISTINCT TitularID INTO #Tit FROM #Alc OPTION (MAXDOP 4);
            CREATE UNIQUE CLUSTERED INDEX IX_Tit ON #Tit (TitularID);

            -- ── 2. Última fila por titular y fuente ─────────────────────────────

            """);

        foreach (var f in fuentes)
        {
            sb.Append($"DROP TABLE IF EXISTS {TablaTemporal(f)};\n");
            sb.Append(UltimaFila(f));
        }

        // Saldo por par: UltimoEstadoTitular es por (TitularID, CarteraID).
        sb.Append("""
            -- ── 3. Saldo (UltimoEstadoTitular) y universo por líder ─────────────
            DROP TABLE IF EXISTS #AlcS;
            -- AgenteID / FechaUltimaGestion / UltimoPerfil: para el ranking de asesores (etapa 4). AgenteID es la
            -- cédula de quien hizo la ÚLTIMA gestión del titular en esa cartera (NULL = sin gestión, marcador
            -- automático o usuario que no está en dbo.Agente).
            SELECT a.TitularID, a.LiderID, a.CarteraID, a.Interna,
                   CAST(CASE WHEN u.SaldoTotal > 0 THEN u.SaldoTotal ELSE 0 END AS decimal(19,2)) AS Saldo,
                   u.AgenteID, u.FechaUltimaGestion, u.UltimoPerfil
              INTO #AlcS
              FROM #Alc a
              LEFT JOIN dbo.UltimoEstadoTitular u ON u.TitularID = a.TitularID AND u.CarteraID = a.CarteraID
            OPTION (MAXDOP 4, HASH JOIN);

            -- #LT: titulares DISTINTOS por líder. LiderID 0 = todos los pares (el mismo titular puede
            -- estar en la misma cartera con dos líderes: su saldo se cuenta una vez por cartera).
            DROP TABLE IF EXISTS #LT;
            SELECT s.LiderID, s.TitularID,
                   MAX(s.Interna) AS EnInterna,
                   CAST(SUM(s.Saldo) AS decimal(19,2)) AS Saldo,
                   CAST(SUM(CASE WHEN s.Interna = 1 THEN s.Saldo ELSE 0 END) AS decimal(19,2)) AS SaldoInt
              INTO #LT
              FROM #AlcS s
             GROUP BY s.LiderID, s.TitularID
            OPTION (MAXDOP 4);

            INSERT INTO #LT (LiderID, TitularID, EnInterna, Saldo, SaldoInt)
            SELECT 0, g.TitularID, MAX(g.Interna), SUM(g.Saldo), SUM(CASE WHEN g.Interna = 1 THEN g.Saldo ELSE 0 END)
              FROM (SELECT TitularID, CarteraID, MAX(Interna) AS Interna, MAX(Saldo) AS Saldo
                      FROM #AlcS GROUP BY TitularID, CarteraID) g
             GROUP BY g.TitularID
            OPTION (MAXDOP 4);

            -- ── 4. Agregados por fuente ─────────────────────────────────────────
            DROP TABLE IF EXISTS #RF;
            CREATE TABLE #RF (
                LiderID int NOT NULL, CarteraID int NOT NULL, Fuente varchar(30) NOT NULL,
                Total int NOT NULL, Buscados int NOT NULL, Encontrados int NOT NULL,
                Viejos90 int NOT NULL, Viejos180 int NOT NULL, Viejos365 int NOT NULL,
                UltimaConsultaMax date NULL);
            DROP TABLE IF EXISTS #RFL;
            CREATE TABLE #RFL (
                LiderID int NOT NULL, Fuente varchar(30) NOT NULL,
                Total int NOT NULL, Buscados int NOT NULL, Encontrados int NOT NULL,
                Viejos90 int NOT NULL, Viejos180 int NOT NULL, Viejos365 int NOT NULL,
                UltimaConsultaMax date NULL);


            """);

        foreach (var f in fuentes)
        {
            sb.Append(Agregado(f, porLider: false));
            sb.Append(Agregado(f, porLider: true));
        }

        // Métricas que cruzan fuentes, por titular.
        sb.Append("""
            -- ── 5. Métricas que cruzan fuentes, por titular ─────────────────────
            -- Con  = celular o email en la última fila de alguna fuente (complemento de SinContacto del Calificador).
            -- ConF = lo mismo, solo en fuentes Funcionales. BusF = buscado en alguna Funcional.
            -- UltFecha = consulta más reciente en cualquier fuente (frescura).
            DROP TABLE IF EXISTS #X;
            SELECT u.TitularID,
                   MAX(u.Con)            AS Con,
                   MAX(u.Con * u.Func)   AS ConF,
                   MAX(u.Func)           AS BusF,
                   MAX(u.Fecha)          AS UltFecha
              INTO #X
              FROM (

            """);

        for (var i = 0; i < fuentes.Count; i++)
        {
            var f = fuentes[i];
            var func = f.Estado == EstadoFuente.Funcional ? 1 : 0;
            sb.Append(i == 0 ? "                    " : "          UNION ALL ");
            sb.Append($"SELECT TitularID, Con, CAST({func} AS tinyint) AS Func, Fecha FROM {TablaTemporal(f)}\n");
        }

        sb.Append($$"""
                   ) u
             GROUP BY u.TitularID
            OPTION (MAXDOP 4);
            CREATE UNIQUE CLUSTERED INDEX IX_X ON #X (TitularID);

            -- ── 6. Por par ──────────────────────────────────────────────────────
            DROP TABLE IF EXISTS #RP;
            SELECT s.LiderID, s.CarteraID,
                   CAST(MAX(s.Interna) AS bit)                                                  AS Interna,
                   COUNT(*)                                                                     AS Total,
                   SUM(ISNULL(CAST(x.Con  AS int), 0))                                          AS ConContacto,
                   SUM(ISNULL(CAST(x.ConF AS int), 0))                                          AS ConContactoFuncional,
                   COUNT(x.TitularID)                                                           AS BuscadosAlguna,
                   SUM(ISNULL(CAST(x.BusF AS int), 0))                                          AS BuscadosFuncional,
                   SUM(CASE WHEN x.UltFecha >= @Lim180 THEN 1 ELSE 0 END)                       AS Frescos180,
                   SUM(CASE WHEN s.Saldo > 0 THEN 1 ELSE 0 END)                                 AS ConSaldo,
                   SUM(CASE WHEN s.Saldo > 0 AND ISNULL(x.Con, 0) = 0 THEN 1 ELSE 0 END)        AS SinContactoConSaldo,
                   CAST(SUM(CASE WHEN ISNULL(x.Con, 0) = 0 THEN s.Saldo ELSE 0 END) AS decimal(19,2)) AS SaldoSinContacto,
                   CAST(SUM(s.Saldo) AS decimal(19,2))                                          AS SaldoTotal
              INTO #RP
              FROM #AlcS s
              LEFT JOIN #X x ON x.TitularID = s.TitularID
             GROUP BY s.LiderID, s.CarteraID
            OPTION (MAXDOP 4, HASH JOIN);

            -- ── 7. Por líder (titulares distintos), con y sin relaciones externas ─
            -- SoloInternas = 1: solo titulares con algún par interno del líder, con el saldo de esos pares.
            DROP TABLE IF EXISTS #RL;
            SELECT q.LiderID, q.SoloInternas,
                   COUNT(*)                                                                     AS Total,
                   SUM(ISNULL(CAST(q.Con  AS int), 0))                                          AS ConContacto,
                   SUM(ISNULL(CAST(q.ConF AS int), 0))                                          AS ConContactoFuncional,
                   SUM(q.Buscado)                                                               AS BuscadosAlguna,
                   SUM(ISNULL(CAST(q.BusF AS int), 0))                                          AS BuscadosFuncional,
                   SUM(CASE WHEN q.UltFecha >= @Lim180 THEN 1 ELSE 0 END)                       AS Frescos180,
                   SUM(CASE WHEN q.Saldo > 0 THEN 1 ELSE 0 END)                                 AS ConSaldo,
                   SUM(CASE WHEN q.Saldo > 0 AND ISNULL(q.Con, 0) = 0 THEN 1 ELSE 0 END)        AS SinContactoConSaldo,
                   CAST(SUM(CASE WHEN ISNULL(q.Con, 0) = 0 THEN q.Saldo ELSE 0 END) AS decimal(19,2)) AS SaldoSinContacto,
                   CAST(SUM(q.Saldo) AS decimal(19,2))                                          AS SaldoTotal
              INTO #RL
              FROM (
                    SELECT lt.LiderID, CAST(0 AS bit) AS SoloInternas, lt.Saldo,
                           x.Con, x.ConF, x.BusF, x.UltFecha, CASE WHEN x.TitularID IS NULL THEN 0 ELSE 1 END AS Buscado
                      FROM #LT lt LEFT JOIN #X x ON x.TitularID = lt.TitularID
                    UNION ALL
                    SELECT lt.LiderID, CAST(1 AS bit), lt.SaldoInt,
                           x.Con, x.ConF, x.BusF, x.UltFecha, CASE WHEN x.TitularID IS NULL THEN 0 ELSE 1 END
                      FROM #LT lt LEFT JOIN #X x ON x.TitularID = lt.TitularID
                     WHERE lt.EnInterna = 1
                   ) q
             GROUP BY q.LiderID, q.SoloInternas
            OPTION (MAXDOP 4, HASH JOIN);

            -- ── 7b. Por par y asesor (etapa 4): último gestor de cada titular ────
            -- "Contacto efectivo" = el UltimoPerfil es de contacto (PerfilesContacto de C#: contacto directo,
            -- acuerdos, tercero/familiar), comparado normalizado. Se clasifican los perfiles DISTINTOS (unos cientos),
            -- no los 3 M de filas.
            DECLARE @PC TABLE (Perfil varchar(60) COLLATE Latin1_General_CI_AI NOT NULL PRIMARY KEY);
            INSERT INTO @PC (Perfil)
            SELECT DISTINCT CAST(value AS varchar(60)) FROM OPENJSON(@PerfilesContacto);

            DROP TABLE IF EXISTS #PerfC;
            SELECT p.UltimoPerfil,
                   CAST(CASE WHEN pc.Perfil IS NOT NULL {{Tercero("n.Perfil")}}THEN 1 ELSE 0 END AS tinyint) AS Contacto
              INTO #PerfC
              FROM (SELECT DISTINCT UltimoPerfil FROM #AlcS WHERE AgenteID IS NOT NULL AND UltimoPerfil IS NOT NULL) p
             CROSS APPLY (SELECT {{GeneradorSqlSugerencias.NormalizarPerfilSql("p.UltimoPerfil")}} AS Perfil) n
              LEFT JOIN @PC pc ON pc.Perfil = n.Perfil
            OPTION (MAXDOP 4);

            DECLARE @LimGestion date = DATEADD(DAY, -@DiasGestion, @Hoy);
            DROP TABLE IF EXISTS #RA;
            SELECT s.LiderID, s.CarteraID, CAST(s.AgenteID AS bigint)                               AS AgenteID,
                   COUNT(*)                                                                     AS Titulares,
                   SUM(ISNULL(CAST(x.Con AS int), 0))                                           AS ConDato,
                   SUM(CASE WHEN s.Saldo > 0 THEN 1 ELSE 0 END)                                 AS ConSaldo,
                   SUM(CASE WHEN s.Saldo > 0 AND ISNULL(x.Con, 0) = 0 THEN 1 ELSE 0 END)        AS SinContactoConSaldo,
                   CAST(SUM(CASE WHEN ISNULL(x.Con, 0) = 0 THEN s.Saldo ELSE 0 END) AS decimal(19,2)) AS SaldoSinContacto,
                   SUM(CASE WHEN s.FechaUltimaGestion >= @LimGestion THEN 1 ELSE 0 END)         AS GestionadosRecientes,
                   SUM(ISNULL(CAST(pc.Contacto AS int), 0))                                     AS ContactoEfectivo
              INTO #RA
              FROM #AlcS s
              LEFT JOIN #X x      ON x.TitularID = s.TitularID
              LEFT JOIN #PerfC pc ON pc.UltimoPerfil = s.UltimoPerfil
             WHERE s.AgenteID IS NOT NULL
             GROUP BY s.LiderID, s.CarteraID, s.AgenteID
            OPTION (MAXDOP 4, HASH JOIN);

            -- ── 8. Escritura: transacción corta, todo o nada ────────────────────
            BEGIN TRANSACTION;

            INSERT INTO {{d.Fuente}} (CorteID, LiderID, CarteraID, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax)
            SELECT @CorteID, LiderID, CarteraID, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax FROM #RF;

            INSERT INTO {{d.FuenteLider}} (CorteID, LiderID, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax)
            SELECT @CorteID, LiderID, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax FROM #RFL;

            INSERT INTO {{d.Par}} (CorteID, LiderID, CarteraID, Interna, Total, ConContacto, ConContactoFuncional, BuscadosAlguna, BuscadosFuncional, Frescos180, ConSaldo, SinContactoConSaldo, SaldoSinContacto, SaldoTotal)
            SELECT @CorteID, LiderID, CarteraID, Interna, Total, ConContacto, ConContactoFuncional, BuscadosAlguna, BuscadosFuncional, Frescos180, ConSaldo, SinContactoConSaldo, SaldoSinContacto, SaldoTotal FROM #RP;

            INSERT INTO {{d.Lider}} (CorteID, LiderID, SoloInternas, Total, ConContacto, ConContactoFuncional, BuscadosAlguna, BuscadosFuncional, Frescos180, ConSaldo, SinContactoConSaldo, SaldoSinContacto, SaldoTotal)
            SELECT @CorteID, LiderID, SoloInternas, Total, ConContacto, ConContactoFuncional, BuscadosAlguna, BuscadosFuncional, Frescos180, ConSaldo, SinContactoConSaldo, SaldoSinContacto, SaldoTotal FROM #RL;

            -- Asesores (etapa 4): opcional, un script aplicado antes de la etapa 4 no la tiene. La sentencia se
            -- compila al llegar a ella (resolución diferida de nombres), así que el IF basta.
            IF {{d.ExisteAsesor}}
                INSERT INTO {{d.Asesor}} (CorteID, LiderID, CarteraID, AgenteID, Titulares, ConDato, ConSaldo, SinContactoConSaldo, SaldoSinContacto, GestionadosRecientes, ContactoEfectivo)
                SELECT @CorteID, LiderID, CarteraID, AgenteID, Titulares, ConDato, ConSaldo, SinContactoConSaldo, SaldoSinContacto, GestionadosRecientes, ContactoEfectivo FROM #RA;

            COMMIT TRANSACTION;

            SELECT (SELECT COUNT(*) FROM #RP) AS Pares, (SELECT COUNT(*) FROM #Tit) AS Titulares;

            """);

        // Limpieza: en producción la conexión se cierra igual; en pruebas deja la sesión limpia.
        sb.Append("DROP TABLE IF EXISTS #Alc, #Tit, #AlcS, #LT, #X, #RF, #RFL, #RP, #RL, #PerfC, #RA");
        foreach (var f in fuentes) sb.Append(", ").Append(TablaTemporal(f));
        sb.Append(";\n");

        return sb.ToString();
    }

    /// <summary>OR sobre las palabras de tercero (<see cref="PerfilesContacto.PalabrasTercero"/>), como el impacto de los envíos.</summary>
    private static string Tercero(string columna) =>
        string.Concat(PerfilesContacto.PalabrasTercero.Select(p => $"OR {columna} LIKE '%{Validar(p)}%' "));

    private static string Validar(string identificador) =>
        Identificador.IsMatch(identificador)
            ? identificador
            : throw new InvalidOperationException($"Identificador SQL no permitido en el catálogo: '{identificador}'.");
}

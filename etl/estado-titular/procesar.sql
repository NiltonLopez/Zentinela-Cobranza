-- ============================================================================
-- Procesamiento de la carga de UltimoEstadoTitular — reemplazo del flujo de datos SSIS
--
-- Se ejecuta en la MISMA sesion donde cargar.ps1 subio #Carga con SqlBulkCopy.
-- Parametro @Modo:
--    'comparar'  arma el resultado y lo compara contra UltimoEstadoTitular. NO escribe
--                nada permanente. Es la prueba de equivalencia contra la carga de SSIS.
--    'publicar'  llena el staging y corre los dos procedimientos de siempre (compuertas
--                de ADR-038 incluidas).
--
-- Cada paso imita un componente del flujo "Cargar Excel a Staging" del paquete
-- Carga_UltimoEstadoTitular.dtsx (leido el 2026-09-25). Las diferencias con SSIS son
-- deliberadas y estan marcadas con [CAMBIO].
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── 1. "Columna derivada" + "Componente de script" ──────────────────────────
-- Conversiones con estilo explicito: el servidor corre en idioma espanol (dmy) y un
-- literal 'yyyy-mm-dd' en datetime se leeria como anio-DIA-mes.
IF OBJECT_ID('tempdb..#Derivada') IS NOT NULL DROP TABLE #Derivada;

SELECT
    c.Archivo,
    c.Linea,
    TRY_CAST(c.identificacion AS BIGINT)                      AS TitularID,          -- (DT_I8)identificacion
    CAST(c.ultimoperfil AS VARCHAR(50))                       AS UltimoPerfil,       -- (DT_STR,50,1252)
    CAST(c.motvnopago   AS VARCHAR(100))                      AS MotivoNoPago,       -- (DT_STR,100,1252)
    CAST(LEFT(c.mejorperfil, 50) AS VARCHAR(50))              AS MejorPerfil,        -- (DT_STR,50,1252)LEFT(x,50)
    TRY_CONVERT(DATE, LTRIM(RTRIM(c.fechagestion)), 23)       AS FechaUltimaGestion, -- (DT_DBDATE)TRIM(x)
    CASE WHEN LEN(n.num) = 10 AND LEFT(n.num, 1) IN ('3', '6')
         THEN CAST(n.num AS VARCHAR(10)) END                   AS NumeroMarcado,      -- sin espacios ni guiones; 10 digitos, empieza en 3 o 6
    TRY_CAST(c.rotacion AS INT)                               AS Rotacion,           -- (DT_I4)rotacion
    CAST(c.ultusugestion AS VARCHAR(50))                      AS UsuarioControlNext, -- (DT_STR,50,1252)ultusugestion
    CAST(c.Tipo_Cartera  AS VARCHAR(100))                     AS Tipo_CarteraC,      -- (DT_STR,100,1252)Tipo_Cartera
    CAST(c.gestion       AS NVARCHAR(500))                    AS Gestion,            -- (DT_WSTR,500)gestion
    TRY_CONVERT(MONEY, c.SaldoTotal)                          AS SaldoTotal,         -- el destino OLE DB convertia texto -> money
    TRY_CONVERT(MONEY, c.valorpromesa)                        AS ValorPromesa,
    TRY_CONVERT(DATE, c.FechaPromesa, 23)                     AS FechaPromesa,       -- ya interpretada en Python como el script
    -- Para diagnosticar rechazos sin tener que volver al archivo:
    CASE WHEN c.identificacion IS NOT NULL AND TRY_CAST(c.identificacion AS BIGINT) IS NULL THEN 1 ELSE 0 END AS TitularNoConvierte
INTO #Derivada
FROM #Carga c
CROSS APPLY (SELECT REPLACE(REPLACE(c.numeromarcado, ' ', ''), '-', '') AS num) n;

-- ── 2. "Division condicional" + "Busqueda CarteraID" + "Busqueda Cartera ID 2" ─
-- En el paquete eran busquedas SIN CACHE: una consulta a la base por fila. Aca son dos
-- tablitas y un JOIN. Si una busqueda devolvia varias filas, SSIS tomaba la primera que
-- llegaba; se imita con MIN(ID), que es el orden del indice agrupado de Cartera.
IF OBJECT_ID('tempdb..#MapArchivo') IS NOT NULL DROP TABLE #MapArchivo;
IF OBJECT_ID('tempdb..#MapTipo')    IS NOT NULL DROP TABLE #MapTipo;

SELECT NombreArchivo, MIN(ID) AS ID
INTO #MapArchivo
FROM dbo.Cartera
WHERE Activa = 1 AND NombreArchivo IS NOT NULL
GROUP BY NombreArchivo;

SELECT NombreCartera, MIN(ID) AS ID
INTO #MapTipo
FROM dbo.Cartera
WHERE Activa = 1
  AND NombreCartera IN ('DELMAR AGORA', 'DELMAR VITTORIA', 'DELMAR CONSULTORIAS', 'DELMAR EXPERTOS',
                        'DELMAR ELITE', 'DELMAR CONTACT', 'ALIARCOM CASTIGADA', 'ALIARCOM PREJURIDICA')
GROUP BY NombreCartera;

IF OBJECT_ID('tempdb..#Resuelta') IS NOT NULL DROP TABLE #Resuelta;

SELECT d.*,
       CASE WHEN d.Tipo_CarteraC IS NULL OR LTRIM(RTRIM(d.Tipo_CarteraC)) = '' THEN 'archivo' ELSE 'tipo' END AS Rama,
       CASE WHEN d.Tipo_CarteraC IS NULL OR LTRIM(RTRIM(d.Tipo_CarteraC)) = '' THEN ma.ID ELSE mt.ID END  AS CarteraID,
       -- "Busqueda Agente": sin coincidencia -> NULL (IgnoreFailure en el paquete).
       TRY_CAST(ag.AgenteID AS INT)                                                                       AS AgenteID
INTO #Resuelta
FROM #Derivada d
LEFT JOIN #MapArchivo ma ON ma.NombreArchivo = d.Archivo
LEFT JOIN #MapTipo    mt ON mt.NombreCartera = d.Tipo_CarteraC
LEFT JOIN dbo.Agente  ag ON ag.UsuarioControlNext = d.UsuarioControlNext;

-- ── 3. Rechazos ──────────────────────────────────────────────────────────────
-- [CAMBIO] En SSIS cada uno de estos casos terminaba mal para TODA la carga:
--   titular-invalido       la conversion (DT_I8) fallaba -> se caia el paquete.
--   cartera-sin-registrar  "Busqueda CarteraID" fallaba -> se caia el paquete (celucredit).
--   tipo-cartera-inactiva  "Busqueda Cartera ID 2" ignoraba el fallo, la fila seguia con
--                          CarteraID nulo, el staging la rechazaba y el destino la mandaba a
--                          una salida de error sin conectar: se perdia en silencio.
-- Ahora la fila mala se aparta, se cuenta y se informa, y el resto se carga.
IF OBJECT_ID('tempdb..#Rechazo') IS NOT NULL DROP TABLE #Rechazo;

SELECT r.Archivo, r.Linea,
       CASE WHEN r.TitularID IS NULL                          THEN 'titular-invalido'
            WHEN r.CarteraID IS NULL AND r.Rama = 'archivo'   THEN 'cartera-sin-registrar'
            WHEN r.CarteraID IS NULL                          THEN 'tipo-cartera-inactiva'
       END AS Motivo,
       r.Tipo_CarteraC
INTO #Rechazo
FROM #Resuelta r
WHERE r.TitularID IS NULL OR r.CarteraID IS NULL;

-- ── 4. Deduplicado ───────────────────────────────────────────────────────────
-- Mismo criterio que sp_Insertar_UltimoEstadoTitular (la fila con la gestion mas
-- reciente), con un desempate que el staging de SSIS no tenia: la ultima fila del
-- archivo. En SSIS los empates los decidia el GETDATE() por fila de la columna derivada,
-- que con ~3 ms de resolucion repite valor en miles de filas: o sea, al azar.
IF OBJECT_ID('tempdb..#Final') IS NOT NULL DROP TABLE #Final;

SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil, SaldoTotal, NumeroMarcado,
       Rotacion, FechaUltimaGestion, FechaPromesa, ValorPromesa, MotivoNoPago, Gestion,
       Archivo, Linea,          -- para volver a la fila del CSV al diagnosticar
       Repeticiones             -- cuantas filas del archivo tenian este (titular, cartera)
INTO #Final
FROM (
    SELECT r.*, ROW_NUMBER() OVER (PARTITION BY r.TitularID, r.CarteraID
                                   ORDER BY r.FechaUltimaGestion DESC, r.Linea DESC) AS rn,
                COUNT(*) OVER (PARTITION BY r.TitularID, r.CarteraID) AS Repeticiones
    FROM #Resuelta r
    WHERE r.TitularID IS NOT NULL AND r.CarteraID IS NOT NULL
) x
WHERE x.rn = 1;

CREATE UNIQUE CLUSTERED INDEX IX_Final ON #Final (TitularID, CarteraID);

-- ── Resumen de la carga (vale para los dos modos) ────────────────────────────
SELECT 'resumen' AS bloque,
       (SELECT COUNT(*) FROM #Carga)                          AS filas_leidas,
       (SELECT COUNT(*) FROM #Rechazo)                        AS filas_rechazadas,
       (SELECT COUNT(*) FROM #Resuelta WHERE TitularID IS NOT NULL AND CarteraID IS NOT NULL) AS filas_validas,
       (SELECT COUNT(*) FROM #Final)                          AS titulares_unicos,
       (SELECT COUNT(DISTINCT CarteraID) FROM #Final)         AS carteras,
       (SELECT COUNT(*) FROM #Resuelta WHERE AgenteID IS NULL AND TitularID IS NOT NULL AND CarteraID IS NOT NULL) AS sin_asesor;

SELECT 'rechazos' AS bloque, Motivo, Archivo, ISNULL(Tipo_CarteraC, '') AS Tipo_Cartera, COUNT(*) AS filas
FROM #Rechazo
GROUP BY Motivo, Archivo, Tipo_CarteraC
ORDER BY COUNT(*) DESC;

-- ============================================================================
IF @Modo = 'comparar'
BEGIN
    -- Prueba de equivalencia contra lo que cargo SSIS, SOLO para las carteras de esta
    -- corrida. No escribe nada permanente. No devuelve datos personales: solo conteos,
    -- montos, fechas, largos y codigos de caracter.
    --
    -- Comparacion NULL-segura con NOT EXISTS (SELECT a INTERSECT SELECT b), y no con
    -- IS DISTINCT FROM: son equivalentes, pero IS DISTINCT FROM es de SQL Server 2022, y
    -- SQL compila el lote ENTERO, asi que una sintaxis nueva rompia tambien el modo
    -- publicar en versiones anteriores. Las diferencias se calculan UNA vez, fila por
    -- fila, en #Diff: una subconsulta no puede ir dentro de un SUM().
    IF OBJECT_ID('tempdb..#Viejo') IS NOT NULL DROP TABLE #Viejo;

    SELECT u.TitularID, u.CarteraID, u.AgenteID, u.UltimoPerfil, u.MejorPerfil, u.SaldoTotal,
           u.NumeroMarcado, u.Rotacion, u.FechaUltimaGestion, u.FechaPromesa, u.ValorPromesa,
           u.MotivoNoPago, u.Gestion
    INTO #Viejo
    FROM dbo.UltimoEstadoTitular u
    WHERE u.CarteraID IN (SELECT DISTINCT CarteraID FROM #Final);

    CREATE UNIQUE CLUSTERED INDEX IX_Viejo ON #Viejo (TitularID, CarteraID);

    IF OBJECT_ID('tempdb..#Diff') IS NOT NULL DROP TABLE #Diff;

    SELECT f.TitularID, f.CarteraID, f.Archivo, f.Linea, f.Repeticiones,
           f.SaldoTotal, f.FechaPromesa, f.ValorPromesa, f.Gestion,
           v.SaldoTotal AS SaldoSsis, v.FechaPromesa AS FPromesaSsis,
           v.ValorPromesa AS VPromesaSsis, v.Gestion AS GestionSsis,
           CASE WHEN v.TitularID IS NULL THEN 1 ELSE 0 END AS SoloNuevo,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.AgenteID           INTERSECT SELECT v.AgenteID)           THEN 1 ELSE 0 END AS dAgenteID,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.UltimoPerfil       INTERSECT SELECT v.UltimoPerfil)       THEN 1 ELSE 0 END AS dUltimoPerfil,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.MejorPerfil        INTERSECT SELECT v.MejorPerfil)        THEN 1 ELSE 0 END AS dMejorPerfil,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.SaldoTotal         INTERSECT SELECT v.SaldoTotal)         THEN 1 ELSE 0 END AS dSaldoTotal,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.NumeroMarcado      INTERSECT SELECT v.NumeroMarcado)      THEN 1 ELSE 0 END AS dNumeroMarcado,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.Rotacion           INTERSECT SELECT v.Rotacion)           THEN 1 ELSE 0 END AS dRotacion,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.FechaUltimaGestion INTERSECT SELECT v.FechaUltimaGestion) THEN 1 ELSE 0 END AS dFechaUltimaGestion,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.FechaPromesa       INTERSECT SELECT v.FechaPromesa)       THEN 1 ELSE 0 END AS dFechaPromesa,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.ValorPromesa       INTERSECT SELECT v.ValorPromesa)       THEN 1 ELSE 0 END AS dValorPromesa,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.MotivoNoPago       INTERSECT SELECT v.MotivoNoPago)       THEN 1 ELSE 0 END AS dMotivoNoPago,
           CASE WHEN v.TitularID IS NOT NULL AND NOT EXISTS (SELECT f.Gestion            INTERSECT SELECT v.Gestion)            THEN 1 ELSE 0 END AS dGestion
    INTO #Diff
    FROM #Final f
    LEFT JOIN #Viejo v ON v.TitularID = f.TitularID AND v.CarteraID = f.CarteraID;

    ALTER TABLE #Diff ADD Alguna AS (dAgenteID + dUltimoPerfil + dMejorPerfil + dSaldoTotal + dNumeroMarcado + dRotacion
                                     + dFechaUltimaGestion + dFechaPromesa + dValorPromesa + dMotivoNoPago + dGestion);

    -- Por cartera: mismos titulares de los dos lados, y cuantos difieren en algo.
    SELECT 'comparacion' AS bloque, d.CarteraID,
           COUNT(*)                                                        AS titulares_nuevo,
           (SELECT COUNT(*) FROM #Viejo v WHERE v.CarteraID = d.CarteraID) AS titulares_ssis,
           SUM(d.SoloNuevo)                                                AS solo_en_nuevo,
           (SELECT COUNT(*) FROM #Viejo v2
             WHERE v2.CarteraID = d.CarteraID
               AND NOT EXISTS (SELECT 1 FROM #Final f2 WHERE f2.TitularID = v2.TitularID AND f2.CarteraID = v2.CarteraID)) AS solo_en_ssis,
           SUM(CASE WHEN d.Alguna > 0 THEN 1 ELSE 0 END)                   AS filas_distintas
    FROM #Diff d
    GROUP BY d.CarteraID
    ORDER BY d.CarteraID;

    -- En que columna difieren.
    SELECT 'columnas' AS bloque,
           SUM(dAgenteID) AS AgenteID, SUM(dUltimoPerfil) AS UltimoPerfil, SUM(dMejorPerfil) AS MejorPerfil,
           SUM(dSaldoTotal) AS SaldoTotal, SUM(dNumeroMarcado) AS NumeroMarcado, SUM(dRotacion) AS Rotacion,
           SUM(dFechaUltimaGestion) AS FechaUltGestion, SUM(dFechaPromesa) AS FechaPromesa,
           SUM(dValorPromesa) AS ValorPromesa, SUM(dMotivoNoPago) AS MotivoNoPago, SUM(dGestion) AS Gestion
    FROM #Diff;

    -- ¿Titulares repetidos en el archivo (donde SSIS elegia al azar cual fila quedaba)
    -- o titulares unicos (diferencia real)?
    SELECT 'diagnostico' AS bloque,
           CASE WHEN Repeticiones > 1 THEN 'titular repetido en el archivo' ELSE 'titular unico' END AS tipo,
           COUNT(*) AS filas, SUM(dSaldoTotal) AS saldo, SUM(dFechaPromesa) AS fecha_promesa,
           SUM(dValorPromesa) AS valor_promesa, SUM(dGestion) AS gestion,
           SUM(dAgenteID + dUltimoPerfil + dMejorPerfil + dNumeroMarcado + dRotacion
               + dFechaUltimaGestion + dMotivoNoPago) AS otras
    FROM #Diff
    WHERE Alguna > 0
    GROUP BY CASE WHEN Repeticiones > 1 THEN 'titular repetido en el archivo' ELSE 'titular unico' END;

    -- ¿La gestion distinta es el recorte a 255 del ACE? Si lo de SSIS es exactamente el
    -- comienzo de lo nuevo, la diferencia es un defecto del camino viejo.
    SELECT 'recorte' AS bloque,
           CASE WHEN Repeticiones > 1 THEN 'repetido' ELSE 'unico' END AS tipo,
           COUNT(*) AS gestion_distinta,
           SUM(CASE WHEN LEN(GestionSsis) <= 255
                     AND LEFT(Gestion, LEN(GestionSsis)) COLLATE Latin1_General_BIN2 = GestionSsis COLLATE Latin1_General_BIN2
                    THEN 1 ELSE 0 END) AS ssis_es_prefijo_a_255,
           MIN(LEN(Gestion)) AS largo_nuevo_min, MAX(LEN(GestionSsis)) AS largo_ssis_max
    FROM #Diff
    WHERE dGestion = 1
    GROUP BY CASE WHEN Repeticiones > 1 THEN 'repetido' ELSE 'unico' END;

    -- Muestra de diferencias en titulares UNICOS que NO son recorte de gestion: montos,
    -- fechas y el texto crudo del CSV. Si esto sale vacio, no hay diferencia real.
    SELECT TOP 15 'muestra' AS bloque, d.CarteraID,
           d.SaldoSsis AS saldo_ssis, d.SaldoTotal AS saldo_nuevo, c.SaldoTotal AS saldo_csv,
           d.FPromesaSsis AS fprom_ssis, d.FechaPromesa AS fprom_nueva, c.FechaPromesa AS fprom_txt,
           d.VPromesaSsis AS vprom_ssis, d.ValorPromesa AS vprom_nuevo, c.valorpromesa AS vprom_csv
    FROM #Diff d
    JOIN #Carga c ON c.Archivo = d.Archivo AND c.Linea = d.Linea
    WHERE d.Repeticiones = 1 AND d.Alguna - d.dGestion > 0
    ORDER BY d.CarteraID, d.Linea;

    RETURN;
END

-- ============================================================================
IF @Modo = 'publicar'
BEGIN
    -- Igual que el paso 1 del paquete: el staging arranca vacio.
    TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;

    INSERT INTO dbo.Staging_UltimoEstadoTitular WITH (TABLOCK)
        (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil, SaldoTotal, NumeroMarcado,
         Rotacion, FechaUltimaGestion, FechaPromesa, ValorPromesa, MotivoNoPago, Gestion, FechaActualizacion)
    SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil, SaldoTotal, NumeroMarcado,
           Rotacion, FechaUltimaGestion, FechaPromesa, ValorPromesa, MotivoNoPago, Gestion, GETDATE()
    FROM #Final;

    -- Los rechazos quedan registrados si la tabla existe (migracion del cargador), con
    -- 30 dias de retencion.
    IF OBJECT_ID('dbo.EtlEstadoTitular_Rechazo', 'U') IS NOT NULL
    BEGIN
        DELETE FROM dbo.EtlEstadoTitular_Rechazo WHERE FechaCarga < DATEADD(DAY, -30, SYSDATETIME());
        INSERT INTO dbo.EtlEstadoTitular_Rechazo (Archivo, Linea, Motivo, TipoCartera)
        SELECT Archivo, Linea, Motivo, Tipo_CarteraC FROM #Rechazo;
    END

    -- Las compuertas y el reemplazo acotado de siempre (ADR-038). Si abortan, lanzan
    -- error y la tabla principal queda intacta.
    EXEC dbo.sp_Insertar_UltimoEstadoTitular   @Origen = 'Cargador';
    EXEC dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Cargador';

    SELECT 'publicado' AS bloque, EjecucionID, Paso, Resultado, FilasStaging, CarterasStaging,
           FilasBorradas, FilasInsertadas, FilasDespues
    FROM dbo.EtlEstadoTitular_Ejecucion
    WHERE EjecucionID > (SELECT MAX(EjecucionID) - 2 FROM dbo.EtlEstadoTitular_Ejecucion)
    ORDER BY EjecucionID;
END

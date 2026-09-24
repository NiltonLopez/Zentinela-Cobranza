-- ============================================================================
-- SIMIT: placas legacy a la forma canónica del Cargue de Fuentes — DBCallCenter
-- (ADR-039, fuente SIMIT)
--
--   QUÉ CORRIGE
--   dbo.SIMIT guarda una fila por (titular, placa). El SP legacy
--   sp_ProcesarSIMITAuxiliar copiaba la columna de placa tal cual venía en el
--   archivo, así que la tabla tiene:
--
--     a) Marcas de texto guardadas como si fueran placas. Medido el 2026-09-24
--        sobre 301.840 filas (47.792 con Placa no nula):
--          'PAZ Y SALV'  5.718 filas ("PAZ Y SALVO" truncado a varchar(10)),
--                        todas EstadoEnSIMIT = 0 y FechaConsulta 2025-07-26.
--          ~152 filas más con EstadoEnSIMIT = 1 (el titular cuenta como
--          ENCONTRADO en SIMIT sin tener vehículo): 'SIN' 35, 'SIM' 12,
--          'NO CARGO' 12, 'NO' 4, 'XXXXXX' 4, '0' 4, '99999' 4, 'N/A' 4,
--          'NO REP' 4, 'TL' 3, 'SIN PLA' 2, 'NO PORT' 2, 'AAAAAA', 'CHASIS',
--          'YWB26¿' (basura de encoding), solo dígitos ('11383', '123456'...),
--          y fragmentos de 2 a 4 o de 8 caracteres ('KZP', 'AEX2', 'AJG1H18A'...).
--        En total 5.870 filas en 5.866 titulares.
--     b) Placas reales escritas con espacio o guion: 'QUR 25', 'CXM 85G',
--        'MCC 18', 'SMG 08B', 'CKH-13', 'UTP-35', 'TTT-06D' (7 filas).
--
--   POR QUÉ
--   El cargue nuevo (PlacaVehiculo.Normalizar + PlacaVehiculo.EsValida en
--   src/Infrastructure/.../Services/Shared/PlacaVehiculo.cs) ya produce solo la
--   forma canónica: sin espacios ni guiones, mayúsculas, 5 a 7 caracteres
--   A-Z/0-9 con al menos una letra y al menos un dígito; lo demás se descarta
--   con advertencia y, si el titular se queda sin placas, entra como UNA fila
--   "sin placa" (Placa NULL, EstadoEnSIMIT 0). Sin este script:
--     - La próxima carga de 'QUR25' no reconoce la fila 'QUR 25' (el refresco
--       compara contenido exacto) e inserta una fila nueva en vez de refrescar
--       la fecha: el titular queda con la placa dos veces.
--     - Las marcas siguen contando como titulares encontrados en SIMIT
--       (EstadoEnSIMIT = 1) en todo lo que lea la tabla.
--
--   QUÉ HACE (todo en UNA transacción, con respaldo)
--     Paso A  placa real no canónica -> forma canónica (quitar espacios/guion,
--             mayúsculas), SOLO si el resultado es una placa válida.     7 filas
--     Paso B  placa que no es placa -> Placa NULL, EstadoEnSIMIT 0 (la
--             representación de "sin placa" del cargue nuevo).        5.870 filas
--     Paso C  dejar a los titulares tocados como los dejaría el cargue nuevo:
--       C1  titular que conserva alguna placa válida: se borra la fila "sin
--           placa" que dejó el paso B (política de vacías: la vacía de un
--           titular con placas no existe).                              75 filas
--       C2  titular sin placas: una sola fila "sin placa" (la de FechaConsulta
--           más reciente). 5.724 de estos titulares YA tenían su fila NULL
--           (la marca era una fila de más) y 3 tenían dos marcas.   5.727 filas
--       C3  placa repetida por titular tras el paso A ('SMG 08B' -> 'SMG08B'
--           ya existía): queda una, la de FechaConsulta más reciente.    1 fila
--     Hoy la tabla NO tiene titulares con dos filas NULL ni (titular, placa)
--     repetidos; C2 y C3 evitan que este script los cree.
--     Resultado esperado: 5.866 + 7 titulares tocados, 5.803 filas menos en
--     dbo.SIMIT (301.840 -> 296.037 con los datos del 2026-09-24).
--
--   QUÉ NO HACE
--     - No corrige errores de digitación que SÍ tienen forma de placa
--       ('IGBZ78D', 'AB3X02', ~160 filas): no se distinguen de un formato raro.
--     - No toca a titulares que no tengan ninguna placa no canónica (p. ej. los
--       69 que ya tenían fila NULL y placas válidas a la vez: estado legacy
--       que el cargue nuevo purga en la próxima carga de ese titular).
--     - No toca SIMITAuxiliar, sp_ProcesarSIMITAuxiliar ni ningún otro objeto.
--       Las vistas que leen dbo.SIMIT lo reflejan solas.
--
--   CÓMO SE CORRE
--     1. Bloque 0 (solo lectura): comparar con las cifras de arriba.
--     2. El resto: aborta sin cambios si ya existe el respaldo, si hay un cargue
--        SIMIT en curso, si los conteos no cuadran o si algo quedó no canónico.
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>   (o abrirlo en SSMS).
--
--   CÓMO REVERTIR
--     El respaldo dbo.SIMIT_Respaldo_20260924 guarda TODAS las filas (antes del
--     cambio) de cada titular tocado. dbo.SIMIT es un heap sin clave, así que se
--     revierte por titular: borrar sus filas actuales y reinsertar las del
--     respaldo. Script al final (comentado). OJO: si después de este script
--     corrió un cargue SIMIT sobre esos titulares, revertir descarta lo que ese
--     cargue les hizo.
--
--   Fecha: 2026-09-24.
-- ============================================================================

USE DBCallCenter;
GO

-- ----------------------------------------------------------------------------
-- Regla (la MISMA de PlacaVehiculo.cs; si una cambia, cambiar la otra):
--   Norm   = UPPER(Placa sin CHAR(32), CHAR(9..13), CHAR(160) ni '-')
--            (los blancos que char.IsWhiteSpace reconoce en el code page 1252
--            de la columna, más el guion)
--   Valida = LEN 5..7, solo [A-Z0-9], al menos una [A-Z] y al menos un [0-9]
--   Todo sobre COLLATE Latin1_General_BIN: en Modern_Spanish_CI_AS los rangos
--   [A-Z] no son fiables ('abc12' NO matchea '%[^A-Z0-9]%' en CI).
--   DATALENGTH y no LEN: LEN ignora los espacios finales, y '=' también, así que
--   'ABC12 ' pasaría por canónica sin DATALENGTH.
-- ----------------------------------------------------------------------------

-- ============================================================================
-- BLOQUE 0. Diagnóstico (solo lectura). Correr antes y comparar.
-- ============================================================================
SET NOCOUNT ON;

;WITH c AS (
    SELECT s.TitularID, s.EstadoEnSIMIT, v.Valida, v.Canonica
      FROM dbo.SIMIT s
     CROSS APPLY (SELECT UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        s.Placa COLLATE Latin1_General_BIN,
                        CHAR(32), ''), CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''),
                        CHAR(13), ''), CHAR(160), ''), '-', '')) AS Norm) n
     CROSS APPLY (SELECT CASE WHEN DATALENGTH(n.Norm) BETWEEN 5 AND 7
                               AND n.Norm NOT LIKE '%[^A-Z0-9]%'
                               AND n.Norm LIKE '%[A-Z]%' AND n.Norm LIKE '%[0-9]%'
                              THEN 1 ELSE 0 END AS Valida,
                         CASE WHEN s.Placa COLLATE Latin1_General_BIN = n.Norm
                               AND DATALENGTH(s.Placa) = DATALENGTH(n.Norm)
                              THEN 1 ELSE 0 END AS Canonica) v
     WHERE s.Placa IS NOT NULL
)
SELECT CASE WHEN Valida = 1 AND Canonica = 1 THEN '1. placa canónica (no se toca)'
            WHEN Valida = 1                  THEN '2. placa real no canónica (paso A)'
            ELSE                                  '3. no es placa (paso B)' END AS Categoria,
       EstadoEnSIMIT,
       COUNT(*)                  AS Filas,        -- esperado: 41.915 / 7 / 5.718 (estado 0) + 152 (estado 1)
       COUNT(DISTINCT TitularID) AS Titulares
  FROM c
 GROUP BY CASE WHEN Valida = 1 AND Canonica = 1 THEN '1. placa canónica (no se toca)'
               WHEN Valida = 1                  THEN '2. placa real no canónica (paso A)'
               ELSE                                  '3. no es placa (paso B)' END,
          EstadoEnSIMIT
 ORDER BY 1, 2;

SELECT COUNT(*) AS FilasSIMIT FROM dbo.SIMIT;     -- esperado: 301.840 (2026-09-24)
GO

-- ============================================================================
-- CORRECCIÓN (una transacción)
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;   -- cualquier error revierte TODO

IF OBJECT_ID('dbo.SIMIT_Respaldo_20260924', 'U') IS NOT NULL
    THROW 50001, 'dbo.SIMIT_Respaldo_20260924 ya existe: el script ya corrió (o quedó a medias). Revisar antes de volver a correrlo; no se pisa el respaldo.', 1;

IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion WHERE Fuente = 'SIMIT' AND Estado = 'EN CURSO')
    THROW 50002, 'Hay un cargue SIMIT EN CURSO (dbo.CargueFuenteEjecucion). Esperar a que termine.', 1;

BEGIN TRAN;

DECLARE @FilasAntes int, @EspA int, @EspB int, @Respaldo int,
        @A int, @B int, @C1 int, @C2 int, @C3 int, @FilasDespues int, @Malas int;

-- Clasificación de las filas con placa no canónica o inválida. dbo.SIMIT es un
-- heap sin clave: los pasos se identifican por predicado y por titular.
-- TABLOCKX en la primera lectura: nadie escribe en SIMIT mientras corre (la
-- transacción es corta: ~6k filas tocadas de ~300k).
DROP TABLE IF EXISTS #Marcadas;
SELECT s.TitularID, v.Valida
  INTO #Marcadas
  FROM dbo.SIMIT s WITH (TABLOCKX, HOLDLOCK)
 CROSS APPLY (SELECT UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    s.Placa COLLATE Latin1_General_BIN,
                    CHAR(32), ''), CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''),
                    CHAR(13), ''), CHAR(160), ''), '-', '')) AS Norm) n
 CROSS APPLY (SELECT CASE WHEN DATALENGTH(n.Norm) BETWEEN 5 AND 7
                           AND n.Norm NOT LIKE '%[^A-Z0-9]%'
                           AND n.Norm LIKE '%[A-Z]%' AND n.Norm LIKE '%[0-9]%'
                          THEN 1 ELSE 0 END AS Valida,
                     CASE WHEN s.Placa COLLATE Latin1_General_BIN = n.Norm
                           AND DATALENGTH(s.Placa) = DATALENGTH(n.Norm)
                          THEN 1 ELSE 0 END AS Canonica) v
 WHERE s.Placa IS NOT NULL
   AND NOT (v.Valida = 1 AND v.Canonica = 1);

SELECT @EspA = SUM(CASE WHEN Valida = 1 THEN 1 ELSE 0 END),
       @EspB = SUM(CASE WHEN Valida = 0 THEN 1 ELSE 0 END)
  FROM #Marcadas;
SELECT @FilasAntes = COUNT(*) FROM dbo.SIMIT;

-- Si los datos cambiaron mucho desde la medición (7 / 5.870), no seguir a ciegas.
IF @EspB > 7000 OR @EspA > 100
    THROW 50003, 'Hay muchas más filas a corregir que las medidas el 2026-09-24 (A=7, B=5.870). Revisar el bloque 0 antes de seguir.', 1;

DROP TABLE IF EXISTS #TitularesA;
DROP TABLE IF EXISTS #TitularesB;
SELECT DISTINCT TitularID INTO #TitularesA FROM #Marcadas WHERE Valida = 1;
SELECT DISTINCT TitularID INTO #TitularesB FROM #Marcadas WHERE Valida = 0;
DECLARE @TitA int = (SELECT COUNT(*) FROM #TitularesA),
        @TitB int = (SELECT COUNT(*) FROM #TitularesB);
PRINT CONCAT('Titulares a tocar: paso A ', @TitA, ', paso B ', @TitB, '  (medido: 7 y 5.866)');

-- Respaldo: TODAS las filas de cada titular tocado, tal como están ahora.
SELECT s.TitularID, s.Placa, s.EstadoEnSIMIT, s.FechaConsulta
  INTO dbo.SIMIT_Respaldo_20260924
  FROM dbo.SIMIT s
 WHERE s.TitularID IN (SELECT TitularID FROM #TitularesA UNION SELECT TitularID FROM #TitularesB);
SET @Respaldo = @@ROWCOUNT;
PRINT CONCAT('Respaldo dbo.SIMIT_Respaldo_20260924: ', @Respaldo, ' filas');

-- ── Paso A: placa real no canónica -> forma canónica ────────────────────────
UPDATE s
   SET s.Placa = n.Norm
  FROM dbo.SIMIT s
 CROSS APPLY (SELECT UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    s.Placa COLLATE Latin1_General_BIN,
                    CHAR(32), ''), CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''),
                    CHAR(13), ''), CHAR(160), ''), '-', '')) AS Norm) n
 WHERE s.Placa IS NOT NULL
   AND DATALENGTH(n.Norm) BETWEEN 5 AND 7
   AND n.Norm NOT LIKE '%[^A-Z0-9]%'
   AND n.Norm LIKE '%[A-Z]%' AND n.Norm LIKE '%[0-9]%'
   AND NOT (s.Placa COLLATE Latin1_General_BIN = n.Norm AND DATALENGTH(s.Placa) = DATALENGTH(n.Norm));
SET @A = @@ROWCOUNT;
PRINT CONCAT('Paso A  placas llevadas a la forma canónica:        ', @A, '  (esperado ', @EspA, ')');

-- ── Paso B: lo que no es placa -> "sin placa" ───────────────────────────────
-- Tras el paso A toda placa válida ya es canónica: lo que quede no válido es marca.
UPDATE s
   SET s.Placa = NULL, s.EstadoEnSIMIT = 0
  FROM dbo.SIMIT s
 CROSS APPLY (SELECT UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    s.Placa COLLATE Latin1_General_BIN,
                    CHAR(32), ''), CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''),
                    CHAR(13), ''), CHAR(160), ''), '-', '')) AS Norm) n
 WHERE s.Placa IS NOT NULL
   AND NOT (DATALENGTH(n.Norm) BETWEEN 5 AND 7
            AND n.Norm NOT LIKE '%[^A-Z0-9]%'
            AND n.Norm LIKE '%[A-Z]%' AND n.Norm LIKE '%[0-9]%');
SET @B = @@ROWCOUNT;
PRINT CONCAT('Paso B  marcas pasadas a Placa NULL, Estado 0:      ', @B, '  (esperado ', @EspB, ')');

-- ── Paso C1: la vacía de un titular que conserva placas no va ───────────────
DELETE s
  FROM dbo.SIMIT s
 WHERE s.Placa IS NULL AND s.EstadoEnSIMIT = 0
   AND s.TitularID IN (SELECT TitularID FROM #TitularesB)
   AND EXISTS (SELECT 1 FROM dbo.SIMIT p WHERE p.TitularID = s.TitularID AND p.Placa IS NOT NULL);
SET @C1 = @@ROWCOUNT;
PRINT CONCAT('Paso C1 vacías de titulares con placas, borradas:  ', @C1, '  (medido: 75)');

-- ── Paso C2: una sola fila "sin placa" por titular (la más reciente) ─────────
;WITH d AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta DESC) AS rn
      FROM dbo.SIMIT s
     WHERE s.Placa IS NULL
       AND s.TitularID IN (SELECT TitularID FROM #TitularesB)
)
DELETE FROM d WHERE rn > 1;
SET @C2 = @@ROWCOUNT;
PRINT CONCAT('Paso C2 filas "sin placa" repetidas, borradas:      ', @C2, '  (medido: 5.727)');

-- ── Paso C3: (titular, placa) repetido por el paso A (la más reciente) ───────
;WITH d AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY s.TitularID, s.Placa ORDER BY s.EstadoEnSIMIT DESC, s.FechaConsulta DESC) AS rn
      FROM dbo.SIMIT s
     WHERE s.Placa IS NOT NULL
       AND s.TitularID IN (SELECT TitularID FROM #TitularesA)
)
DELETE FROM d WHERE rn > 1;
SET @C3 = @@ROWCOUNT;
PRINT CONCAT('Paso C3 placas repetidas tras el paso A, borradas:  ', @C3, '  (medido: 1)');

-- ── Verificación: si algo no cuadra, THROW -> rollback de todo ──────────────
SELECT @FilasDespues = COUNT(*) FROM dbo.SIMIT;

-- 0 placas no nulas fuera de la forma canónica, en TODA la tabla.
SELECT @Malas = COUNT(*)
  FROM dbo.SIMIT s
 CROSS APPLY (SELECT UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    s.Placa COLLATE Latin1_General_BIN,
                    CHAR(32), ''), CHAR(9), ''), CHAR(10), ''), CHAR(11), ''), CHAR(12), ''),
                    CHAR(13), ''), CHAR(160), ''), '-', '')) AS Norm) n
 WHERE s.Placa IS NOT NULL
   AND NOT (DATALENGTH(n.Norm) BETWEEN 5 AND 7
            AND n.Norm NOT LIKE '%[^A-Z0-9]%'
            AND n.Norm LIKE '%[A-Z]%' AND n.Norm LIKE '%[0-9]%'
            AND s.Placa COLLATE Latin1_General_BIN = n.Norm
            AND DATALENGTH(s.Placa) = DATALENGTH(n.Norm));
PRINT CONCAT('Verificación: placas no canónicas restantes:        ', @Malas);
PRINT CONCAT('Verificación: filas en dbo.SIMIT ', @FilasAntes, ' -> ', @FilasDespues);

IF @A <> @EspA OR @B <> @EspB
    THROW 50010, 'Los pasos A/B no tocaron las filas clasificadas. Rollback.', 1;
IF @Malas <> 0
    THROW 50011, 'Quedan placas no canónicas en dbo.SIMIT. Rollback.', 1;
IF @FilasDespues <> @FilasAntes - @C1 - @C2 - @C3
    THROW 50012, 'El conteo de dbo.SIMIT no cuadra con lo borrado. Rollback.', 1;
-- Ningún titular tocado desaparece de SIMIT (a lo sumo pasa a "sin placa").
IF EXISTS (SELECT 1 FROM #TitularesB b WHERE NOT EXISTS (SELECT 1 FROM dbo.SIMIT s WHERE s.TitularID = b.TitularID))
    THROW 50013, 'Un titular tocado se quedó sin filas en dbo.SIMIT. Rollback.', 1;
-- Titulares del paso B: o una sola fila "sin placa", o solo placas.
IF EXISTS (SELECT 1 FROM dbo.SIMIT s
            WHERE s.TitularID IN (SELECT TitularID FROM #TitularesB)
            GROUP BY s.TitularID
           HAVING SUM(CASE WHEN s.Placa IS NULL THEN 1 ELSE 0 END) > 1
               OR (SUM(CASE WHEN s.Placa IS NULL THEN 1 ELSE 0 END) = 1 AND COUNT(*) > 1))
    THROW 50014, 'Hay titulares con varias filas "sin placa" o con "sin placa" y placas. Rollback.', 1;
-- Ningún (titular, placa) repetido en los titulares tocados.
IF EXISTS (SELECT 1 FROM dbo.SIMIT s
            WHERE s.Placa IS NOT NULL
              AND s.TitularID IN (SELECT TitularID FROM #TitularesA UNION SELECT TitularID FROM #TitularesB)
            GROUP BY s.TitularID, s.Placa HAVING COUNT(*) > 1)
    THROW 50015, 'Hay (titular, placa) repetidos en los titulares tocados. Rollback.', 1;

COMMIT TRAN;
PRINT 'OK: cambios confirmados. Respaldo en dbo.SIMIT_Respaldo_20260924.';
GO

-- ============================================================================
-- REVERSIÓN (comentada). Devuelve cada titular tocado a como estaba antes.
-- Revisar primero si corrió algún cargue SIMIT después de este script:
--   SELECT * FROM dbo.CargueFuenteEjecucion
--    WHERE Fuente = 'SIMIT' AND FechaInicio >= '2026-09-24' ORDER BY EjecucionID;
-- Lo que esos cargues hayan hecho sobre estos titulares se pierde al revertir.
-- ============================================================================
/*
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @Respaldo int = (SELECT COUNT(*) FROM dbo.SIMIT_Respaldo_20260924);

DELETE s
  FROM dbo.SIMIT s
 WHERE EXISTS (SELECT 1 FROM dbo.SIMIT_Respaldo_20260924 r WHERE r.TitularID = s.TitularID);
PRINT CONCAT('Filas actuales de los titulares respaldados, borradas: ', @@ROWCOUNT);

INSERT INTO dbo.SIMIT (TitularID, Placa, EstadoEnSIMIT, FechaConsulta)
SELECT TitularID, Placa, EstadoEnSIMIT, FechaConsulta
  FROM dbo.SIMIT_Respaldo_20260924;
IF @@ROWCOUNT <> @Respaldo
    THROW 50020, 'No se reinsertó todo el respaldo. Rollback.', 1;
PRINT CONCAT('Filas del respaldo reinsertadas: ', @Respaldo);

COMMIT TRAN;
-- Cuando ya no haga falta: DROP TABLE dbo.SIMIT_Respaldo_20260924;
*/

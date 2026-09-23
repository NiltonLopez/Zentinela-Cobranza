-- ============================================================================
-- Recuperación de UltimoEstadoTitular desde el histórico — DBCallCenter (ADR-038)
--
--   La corrida del 18-sep dejó UltimoEstadoTitular en CERO filas y el Comparador
--   de Carteras inservible. Los Excel de origen ya no están, así que no se puede
--   repetir la carga tal cual.
--
--   Pero HistoricoEstadoTitular sí sobrevivió: guarda, por (TitularID, CarteraID),
--   el último estado conocido. Con eso se reconstruye UltimoEstadoTitular con
--   fecha de corte del último día cargado (2026-09-11 al momento de escribir esto).
--
--   NO es magia: los datos quedan con la antigüedad del histórico, no al día. La
--   próxima corrida buena del ETL los pone al corriente. Lo que esto compra es
--   que los módulos vuelvan a responder mientras tanto, con datos reales, en vez
--   de no responder nada.
--
--   MEDIDO EN SECO CONTRA LA BASE REAL el 2026-09-23 (solo lectura, 3,7 s):
--
--     2.708.747 filas | 24 carteras | 2.003.771 titulares
--     antigüedades entre 2026-06-18 y 2026-09-11
--
--   Dos cosas que se siguen de esos números y conviene tener presentes:
--
--   a) NO es un corte único. Cada titular queda con la fecha de su última
--      gestión conocida, que para algunos es de junio. Es la definición misma de
--      "último estado conocido", pero no confundirlo con una foto del 11-sep.
--
--   b) Son más carteras (24) y más filas de las que traía el ETL en su última
--      corrida buena (22 carteras). La diferencia son titulares que el origen ya
--      había dejado de mandar y que acá reviven. Se corrige solo: como el
--      reemplazo es acotado por cartera, la próxima corrida buena borra y
--      reinserta esas carteras completas y los deja fuera.
--
--   Las compuertas ya se verificaron contra estos números: la referencia son 22
--   carteras, el mínimo exigido 20, y la reconstrucción trae 24. Pasa sin forzar.
--
--   Límite conocido: en el histórico SaldoTotal y ValorPromesa son DECIMAL(18,0)
--   y en UltimoEstadoTitular son MONEY. Los centavos ya se habían perdido al
--   entrar al histórico y no vuelven. Para saldos de cobranza en pesos es
--   irrelevante, pero queda dicho.
--
--   La reconstrucción entra por el MISMO camino que el ETL — staging y luego
--   sp_Insertar_UltimoEstadoTitular — para que pase por las mismas compuertas y
--   quede en la misma auditoría, marcada como Origen = 'Recuperacion'.
--
--   Requiere haber aplicado antes 2026-09-23_rediseno_etl_estado_titular.sql.
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>
-- ============================================================================

USE DBCallCenter;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.sp_Insertar_UltimoEstadoTitular') IS NULL
   OR OBJECT_ID('dbo.EtlEstadoTitular_Ejecucion', 'U') IS NULL
BEGIN
    RAISERROR('Falta aplicar 2026-09-23_rediseno_etl_estado_titular.sql antes de este script.', 16, 1);
    SET NOEXEC ON;
END
GO

-- ── 1. Respaldar lo que haya en el staging ──────────────────────────────────
-- Al momento de escribir esto el staging tiene 4 carteras de una corrida que se
-- cayó por falta de memoria. Son más recientes que el histórico pero cubren
-- menos de una quinta parte del universo: mezclarlas daría una tabla con dos
-- antigüedades distintas y sin forma de saber cuál es cuál. Se guardan aparte en
-- vez de perderlas.
IF EXISTS (SELECT 1 FROM dbo.Staging_UltimoEstadoTitular)
BEGIN
    DECLARE @respaldo SYSNAME = 'Staging_UltimoEstadoTitular_Respaldo_' + FORMAT(GETDATE(), 'yyyyMMdd_HHmm');
    DECLARE @sql NVARCHAR(400) =
        N'SELECT * INTO dbo.' + QUOTENAME(@respaldo) + N' FROM dbo.Staging_UltimoEstadoTitular;';
    EXEC sp_executesql @sql;
    PRINT 'Staging respaldado en dbo.' + @respaldo;
END
GO

-- ── 2. Reconstruir el último estado conocido ────────────────────────────────
TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;

INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
     SaldoTotal, NumeroMarcado, Rotacion,
     FechaUltimaGestion, FechaPromesa, ValorPromesa,
     MotivoNoPago, Gestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
       SaldoTotal, NumeroMarcado, Rotacion,
       FechaUltimaGestion, FechaPromesa, ValorPromesa,
       MotivoNoPago, Gestion, FechaActualizacion
FROM (
    SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
           SaldoTotal, NumeroMarcado, Rotacion,
           FechaUltimaGestion, FechaPromesa, ValorPromesa,
           MotivoNoPago, Gestion, FechaActualizacion,
           ROW_NUMBER() OVER (
               PARTITION BY TitularID, CarteraID
               ORDER BY FechaActualizacion DESC, Rotacion DESC) AS rn
    FROM dbo.HistoricoEstadoTitular
) h
WHERE h.rn = 1
OPTION (MAXDOP 4);

PRINT 'Reconstruccion cargada al staging:';
SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras,
       MIN(FechaActualizacion) AS corte_mas_viejo, MAX(FechaActualizacion) AS corte_mas_nuevo
FROM dbo.Staging_UltimoEstadoTitular;
GO

-- ── 3. Publicar por el camino normal ────────────────────────────────────────
-- Si las compuertas se quejan, el procedimiento aborta y no toca nada: revisar
-- el motivo en EtlEstadoTitular_Ejecucion antes de insistir con @Forzar = 1.
EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'Recuperacion';
GO

-- ── 4. Verificación ─────────────────────────────────────────────────────────
SELECT COUNT(*) AS filas_ultimo, COUNT(DISTINCT CarteraID) AS carteras,
       MAX(FechaActualizacion) AS datos_al
FROM dbo.UltimoEstadoTitular;

SELECT TOP (3) EjecucionID, Paso, Origen, Resultado, Motivo,
       FilasStaging, CarterasStaging, FilasInsertadas, FechaFin
FROM dbo.EtlEstadoTitular_Ejecucion
ORDER BY EjecucionID DESC;
GO

-- ── 5. Liberar el staging ───────────────────────────────────────────────────
-- Imprescindible, y por poco se me pasa. En el ciclo normal el staging lo vacía
-- sp_Insertar_HistoricoEstadoTitular al final, pero acá ese paso NO corre (ver
-- la nota de abajo), así que la reconstrucción se quedaría adentro. Si el
-- paquete SSIS no trunca al arrancar — y no está verificado que lo haga —, la
-- próxima corrida mezclaría estos datos de junio-septiembre con los Excel del
-- día, y el dedup no lo notaría porque son (TitularID, CarteraID) válidos.
--
-- Solo se vacía si la publicación terminó OK: si abortó, estos datos son lo
-- único que hay y se conservan para reintentar.
IF EXISTS (SELECT 1 FROM dbo.EtlEstadoTitular_Ejecucion
           WHERE Paso = 'UltimoEstadoTitular' AND Origen = 'Recuperacion'
             AND Resultado = 'OK'
             AND EjecucionID = (SELECT MAX(EjecucionID) FROM dbo.EtlEstadoTitular_Ejecucion))
BEGIN
    TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
    PRINT 'Staging liberado: la recuperacion se publico correctamente.';
END
ELSE
    PRINT 'Staging INTACTO a proposito: la recuperacion no termino en OK. Revisar EtlEstadoTitular_Ejecucion.';
GO

-- NO se ejecuta sp_Insertar_HistoricoEstadoTitular en ningún momento: el
-- histórico es el origen de esta reconstrucción, y volver a pasarlo por ahí solo
-- le correría las fechas a filas que ya estaban bien. La próxima corrida real
-- del ETL retoma el ciclo normal.
SET NOEXEC OFF;
GO

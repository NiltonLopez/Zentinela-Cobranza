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
--   que los módulos vuelvan a responder mientras tanto, con datos reales y de una
--   sola fecha, en vez de no responder nada.
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

-- NO se ejecuta sp_Insertar_HistoricoEstadoTitular a continuación: el histórico
-- es el origen de esta reconstrucción, y volver a pasarlo por ahí solo le
-- correría las fechas a filas que ya estaban bien. La próxima corrida real del
-- ETL retoma el ciclo normal.
SET NOEXEC OFF;
GO

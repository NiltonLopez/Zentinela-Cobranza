-- ============================================================================
-- ROLLBACK del rediseño del ETL de estado de titulares (ADR-038)
--
--   Restaura los dos procedimientos EXACTAMENTE como estaban el 2026-09-23,
--   copiados de OBJECT_DEFINITION antes de tocar nada.
--
--   Deja en pie EtlEstadoTitular_Ejecucion y vw_EtlEstadoTitularEstado: no
--   estorban, nadie los escribe si los SP viejos vuelven, y la auditoría de lo
--   que pasó mientras el rediseño estuvo activo es justamente lo que se querría
--   conservar para entender por qué se revirtió. Al final del archivo quedan
--   comentados los DROP, por si se quiere una reversión total.
--
--   ADVERTENCIA: volver atrás reinstala el comportamiento que vació la tabla el
--   18 de septiembre — TRUNCATE incondicional, sin transacción y sin validar que
--   el staging tenga datos.
--
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>
-- ============================================================================

USE DBCallCenter;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Insertar_UltimoEstadoTitular]
AS
BEGIN
    TRUNCATE TABLE UltimoEstadoTitular;

    ;WITH CTE AS (
        SELECT *,
               ROW_NUMBER() OVER (
                   PARTITION BY TitularID, CarteraID
                   ORDER BY FechaUltimaGestion DESC
               ) AS rn
        FROM Staging_UltimoEstadoTitular
    )
    INSERT INTO UltimoEstadoTitular (
        TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
        SaldoTotal, NumeroMarcado, Rotacion,
        FechaUltimaGestion, FechaPromesa, ValorPromesa,
        MotivoNoPago, Gestion, FechaActualizacion
    )
    SELECT
        TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
        SaldoTotal, NumeroMarcado, Rotacion,
        FechaUltimaGestion, FechaPromesa, ValorPromesa,
        MotivoNoPago, Gestion, FechaActualizacion
    FROM CTE
    WHERE rn = 1;

    --TRUNCATE TABLE Staging_UltimoEstadoTitular;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Insertar_HistoricoEstadoTitular]
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- PURGA: registros con más de 3 meses
        IF EXISTS (
            SELECT 1
            FROM HistoricoEstadoTitular
            WHERE FechaActualizacion < DATEADD(month, -3, GETDATE())
        )
        BEGIN
            DELETE FROM HistoricoEstadoTitular
            WHERE FechaActualizacion < DATEADD(month, -3, GETDATE());
        END;

        -- Identificamos el ÚLTIMO registro histórico (el más reciente) por Titular+Cartera
        SELECT H.TitularID, H.CarteraID, H.Rotacion AS UltimaRotacion, H.FechaActualizacion AS UltimaFecha
        INTO #UltimoHistorico
        FROM HistoricoEstadoTitular H
        INNER JOIN (
            SELECT TitularID, CarteraID, MAX(FechaActualizacion) AS MaxFecha
            FROM HistoricoEstadoTitular
            GROUP BY TitularID, CarteraID
        ) M
            ON H.TitularID = M.TitularID
           AND H.CarteraID = M.CarteraID
           AND H.FechaActualizacion = M.MaxFecha;

        -- 1. ACTUALIZAR: si la rotación de hoy es IGUAL a la del último registro histórico,
        --    NO se inserta nada nuevo, solo se refresca la fecha de actualización.
        UPDATE H
        SET H.FechaActualizacion = U.FechaActualizacion ,
            H.Gestion = U.Gestion
        FROM HistoricoEstadoTitular H
        INNER JOIN #UltimoHistorico UH
            ON H.TitularID = UH.TitularID
           AND H.CarteraID = UH.CarteraID
           AND H.FechaActualizacion = UH.UltimaFecha
        INNER JOIN UltimoEstadoTitular U
            ON UH.TitularID = U.TitularID
           AND UH.CarteraID = U.CarteraID
           AND ISNULL(UH.UltimaRotacion, -1) = ISNULL(U.Rotacion, -1);

        -- 2. INSERTAR: si la rotación de hoy es DIFERENTE a la del último registro
        --    (o si el titular/cartera no tiene histórico todavía), se inserta un nuevo
        --    renglón y el anterior se conserva intacto.
        INSERT INTO HistoricoEstadoTitular (
            TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
            SaldoTotal, NumeroMarcado, Rotacion,
            FechaUltimaGestion, FechaPromesa, ValorPromesa,
            MotivoNoPago, Gestion, FechaActualizacion
        )
        SELECT
            U.TitularID, U.CarteraID, U.AgenteID, U.UltimoPerfil, U.MejorPerfil,
            U.SaldoTotal, U.NumeroMarcado, U.Rotacion,
            U.FechaUltimaGestion, U.FechaPromesa, U.ValorPromesa,
            U.MotivoNoPago, U.Gestion, U.FechaActualizacion
        FROM UltimoEstadoTitular U
        LEFT JOIN #UltimoHistorico UH
            ON U.TitularID = UH.TitularID
           AND U.CarteraID = UH.CarteraID
        WHERE UH.TitularID IS NULL                                     -- no tiene histórico aún
           OR ISNULL(UH.UltimaRotacion, -1) <> ISNULL(U.Rotacion, -1); -- rotación cambió

        DROP TABLE #UltimoHistorico;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        IF OBJECT_ID('tempdb..#UltimoHistorico') IS NOT NULL
            DROP TABLE #UltimoHistorico;

        THROW;
    END CATCH

    TRUNCATE TABLE Staging_UltimoEstadoTitular;
END;
GO

-- Reversión total (descomentar solo si además se quiere borrar la auditoría):
-- DROP VIEW  IF EXISTS dbo.vw_EtlEstadoTitularEstado;
-- DROP TABLE IF EXISTS dbo.EtlEstadoTitular_Ejecucion;
-- GO

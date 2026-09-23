-- ============================================================================
-- Prueba de regresión: demuestra que los procedimientos ORIGINALES destruían la
-- tabla sin lanzar error. Es el incidente del 18-sep reproducido en laboratorio.
--
-- Correr DESPUÉS de 01_setup.sql y del script _ROLLBACK (con USE reemplazado por
-- EtlHarness), que es el que instala los procedimientos viejos. Ver README.md.
--
-- Resultado esperado: 2201 -> 0 filas, error lanzado: 0.
-- ============================================================================
USE EtlHarness;
GO
SET NOCOUNT ON;

DECLARE @antes INT, @despues INT, @err INT = 0;

SELECT @antes = COUNT(*) FROM dbo.UltimoEstadoTitular;

-- El Foreach no encontró ningún Excel: el staging queda vacío.
TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;

BEGIN TRY
    EXEC dbo.sp_Insertar_UltimoEstadoTitular;
END TRY
BEGIN CATCH
    SET @err = ERROR_NUMBER();
END CATCH

SELECT @despues = COUNT(*) FROM dbo.UltimoEstadoTitular;

SELECT 'PROCEDIMIENTOS VIEJOS' AS version,
       @antes    AS filas_antes,
       @despues  AS filas_despues,
       @err      AS error_lanzado,
       CASE WHEN @despues = 0 AND @err = 0
            THEN 'DESTRUYO LA TABLA Y REPORTO EXITO'
            ELSE 'comportamiento distinto al del incidente' END AS veredicto;
GO

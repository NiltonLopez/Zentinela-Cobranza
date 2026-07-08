-- =====================================================================
-- Migración: 2026-07-08 — Índice faltante en dbo.PositivaEmpresas
-- =====================================================================
-- Motivo: docs/schema.md ya documentaba `IX_PositivaEmpresas_TitularID_Fecha`
--   como parte del patrón aplicado a las 15 tablas fuente de vw_BaseMadre
--   (sección "Calificador de Cartera — tablas fuente de vw_BaseMadre"), pero
--   en producción la tabla seguía siendo HEAP sin ningún índice. Drift entre
--   documentación y estado real — no se encontró migración previa que lo
--   creara. Detectado durante incidente de timeouts en Calificador
--   (ver memoria project_calificador_retry_storm.md): cada corrida forzaba
--   un table scan completo de 1.9M filas para el bloque de localización
--   (result set 9 de CalificadorCarteraService.SqlAgregados), a diferencia
--   de las otras 14 fuentes que sí tenían el índice.
--
-- Idempotente: IF NOT EXISTS.
-- Reversible: DROP INDEX al final (comentado).
--
-- BD: DBCallCenter (10.0.0.89,1433). Aplicar como sa o equivalente.
-- =====================================================================

USE DBCallCenter;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PositivaEmpresas_TitularID_Fecha')
    CREATE NONCLUSTERED INDEX IX_PositivaEmpresas_TitularID_Fecha
        ON dbo.PositivaEmpresas (TitularID, FechaConsulta);
GO

-- Rollback:
-- DROP INDEX IX_PositivaEmpresas_TitularID_Fecha ON dbo.PositivaEmpresas;

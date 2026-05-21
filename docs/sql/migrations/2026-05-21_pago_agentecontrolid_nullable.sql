-- 2026-05-21 — AgenteControlID nullable en dbo.Pago
--
-- Motivación: pagos con UsuarioControl = "BASE GENERAL" no tienen un agente
-- responsable (el mérito va solo al líder). Hasta ahora la columna era NOT NULL,
-- forzando un agente para todo pago. Esta migración la hace nullable para
-- representar correctamente esos pagos.
--
-- BD: DBCallCenter. Sin downtime (ALTER COLUMN sin reescritura, MS SQL Server).
--
-- Verificación rápida posterior:
--   SELECT COUNT(*) FROM dbo.Pago WHERE AgenteControlID IS NULL;  -- esperado 0 inicialmente
--
-- Reversión:
--   UPDATE dbo.Pago SET AgenteControlID = 0 WHERE AgenteControlID IS NULL;  -- si hay filas BASE GENERAL ya cargadas
--   ALTER TABLE dbo.Pago ALTER COLUMN AgenteControlID BIGINT NOT NULL;

USE DBCallCenter;
GO

ALTER TABLE dbo.Pago ALTER COLUMN AgenteControlID BIGINT NULL;
GO

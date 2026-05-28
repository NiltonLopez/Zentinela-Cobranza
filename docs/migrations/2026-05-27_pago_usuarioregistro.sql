-- =============================================================================
-- Migración manual: Pago.UsuarioControlID (INT, Id de usuario)
--                   → Pago.UsuarioRegistro (NVARCHAR(256), UserName de Identity)
--
-- Contexto: antes guardábamos el Id del ApplicationUser que confirmaba el cargue.
-- Ahora guardamos su UserName (login de Identity), más legible en reportes.
-- El UserName vive en DBZentinela.dbo.AspNetUsers (cross-DB, sin FK de BD).
--
-- Ejecutar manualmente por el DBA. Idempotente: revisa existencia antes de actuar.
-- Hace backfill de las filas existentes (Id → UserName) antes de borrar la columna vieja.
-- =============================================================================

USE DBCallCenter;
GO

-- 1. Agregar la nueva columna como NULL temporalmente (para poder hacer backfill).
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Pago') AND name = 'UsuarioRegistro')
BEGIN
    ALTER TABLE dbo.Pago ADD UsuarioRegistro NVARCHAR(256) NULL;
END
GO

-- 2. Backfill: traducir el Id guardado en UsuarioControlID al UserName correspondiente.
--    JOIN cross-database a la tabla de Identity en DBZentinela.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Pago') AND name = 'UsuarioControlID')
BEGIN
    UPDATE p
    SET p.UsuarioRegistro = u.UserName
    FROM dbo.Pago p
    INNER JOIN DBZentinela.dbo.AspNetUsers u ON u.Id = p.UsuarioControlID
    WHERE p.UsuarioRegistro IS NULL;

    -- 3. Fallback para filas huérfanas (Id sin usuario en AspNetUsers):
    --    conservar el Id original como texto para no perder trazabilidad.
    UPDATE dbo.Pago
    SET UsuarioRegistro = CONCAT('ID:', UsuarioControlID)
    WHERE UsuarioRegistro IS NULL;
END
GO

-- 4. Endurecer a NOT NULL una vez que toda fila tiene valor.
ALTER TABLE dbo.Pago ALTER COLUMN UsuarioRegistro NVARCHAR(256) NOT NULL;
GO

-- 5. Borrar la columna vieja.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Pago') AND name = 'UsuarioControlID')
BEGIN
    ALTER TABLE dbo.Pago DROP COLUMN UsuarioControlID;
END
GO

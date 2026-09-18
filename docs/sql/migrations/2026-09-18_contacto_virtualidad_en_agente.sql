-- ============================================================================
-- Contacto de virtualidad pasa de AgenteContactoVirtualidad a columnas de Agente
--
--   Reemplaza la tabla 1:1 creada esa misma manana por
--   2026-09-18_creador_virtualidad_sms.sql. Se verifico en el servidor que
--   NADIE mas escribe en dbo.Agente (sin sp/fn/vistas/triggers/jobs que la
--   inserten, sin referencias desde otras BDs; el unico escritor es
--   Zentinela.RegistroService via EF, con lista de columnas explicita), asi
--   que agregar columnas NULL no rompe a nadie. Precedente: Lider_Cartera
--   recibio EsExterna y Activo de la misma forma.
--
--   Idempotente. Migra los datos existentes antes de borrar la tabla.
--   Fecha: 2026-09-18.
-- ============================================================================

IF COL_LENGTH('dbo.Agente', 'NumeroCorporativo') IS NULL
BEGIN
    ALTER TABLE dbo.Agente ADD
        NumeroCorporativo      VARCHAR(10)  NULL,
        LinkWhatsapp           VARCHAR(100) NULL,
        ContactoActualizadoEn  DATETIME2    NULL,
        ContactoActualizadoPor VARCHAR(100) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Agente_NumeroCorporativo')
BEGIN
    ALTER TABLE dbo.Agente ADD CONSTRAINT CK_Agente_NumeroCorporativo
        CHECK (NumeroCorporativo IS NULL
               OR (LEN(NumeroCorporativo) = 10
                   AND NumeroCorporativo LIKE '3[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'));
END
GO

-- Migrar lo que ya se hubiera guardado en la tabla aparte.
IF OBJECT_ID('dbo.AgenteContactoVirtualidad', 'U') IS NOT NULL
BEGIN
    UPDATE a
       SET a.NumeroCorporativo      = c.NumeroCorporativo,
           a.LinkWhatsapp           = c.LinkWhatsapp,
           a.ContactoActualizadoEn  = c.FechaActualizacion,
           a.ContactoActualizadoPor = c.UsuarioActualizacion
      FROM dbo.Agente a
      JOIN dbo.AgenteContactoVirtualidad c ON c.AgenteID = a.AgenteID;

    DROP TABLE dbo.AgenteContactoVirtualidad;
END
GO

-- Verificacion
SELECT COUNT(*) AS agentes, SUM(CASE WHEN NumeroCorporativo IS NOT NULL THEN 1 ELSE 0 END) AS con_contacto
  FROM dbo.Agente;
SELECT OBJECT_ID('dbo.AgenteContactoVirtualidad', 'U') AS tabla_vieja_debe_ser_null;
GO

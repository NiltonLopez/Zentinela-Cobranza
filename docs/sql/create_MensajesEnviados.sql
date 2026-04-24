-- ============================================================
-- Tabla MensajesEnviados
-- Registro de mensajes salientes a titulares.
-- Almacena metadatos del envío SIN el contenido del mensaje.
-- Canal permite extender a WhatsApp, Email, etc. en el futuro.
-- Ejecutar en: DBCallCenter
-- ============================================================

-- Recrear limpia (solo si hay datos de prueba que se pueden perder)
IF OBJECT_ID('dbo.MensajesEnviados', 'U') IS NOT NULL
    DROP TABLE dbo.MensajesEnviados;

-- Tabla sin PK surrogate — solo inserción y consulta en agregados.
-- EF Core usa HasNoKey() para esta entidad.
CREATE TABLE dbo.MensajesEnviados (
    NumeroDocumento BIGINT        NOT NULL,
    NumeroTelefono  NVARCHAR(20)  NOT NULL,
    CarteraID       INT               NULL,
    NombreCartera   NVARCHAR(100) NOT NULL,
    FechaEnvio      DATE          NOT NULL,
    Canal           NVARCHAR(20)  NOT NULL DEFAULT 'SMS',
    FechaCargue     DATETIME2     NOT NULL DEFAULT GETDATE()
);

-- Índice para consultas del informe (filtro por cartera + fecha)
CREATE INDEX IX_MensajesEnviados_CarteraID_FechaEnvio
    ON dbo.MensajesEnviados (CarteraID, FechaEnvio)
    INCLUDE (NumeroTelefono, NombreCartera, Canal);

-- Índice para detección de alertas (teléfono + cartera)
CREATE INDEX IX_MensajesEnviados_Telefono_Cartera
    ON dbo.MensajesEnviados (NumeroTelefono, NombreCartera);

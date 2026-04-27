-- ============================================================
-- Tabla MensajesEnviados
-- Registro de mensajes salientes a titulares.
-- Almacena metadatos del envío SIN el contenido del mensaje.
-- Canal permite extender a WhatsApp, Email, etc. en el futuro.
--
-- La carga se alimenta seleccionando Líder + Cartera, por eso la
-- tabla referencia a dbo.Lider_Cartera mediante la PK compuesta
-- (LiderID, CarteraID).
--
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
    LiderID         INT           NOT NULL,
    CarteraID       INT           NOT NULL,
    FechaEnvio      DATE          NOT NULL,
    Canal           NVARCHAR(20)  NOT NULL DEFAULT 'SMS',
    FechaCargue     DATETIME2     NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_MensajesEnviados_LiderCartera
        FOREIGN KEY (LiderID, CarteraID)
        REFERENCES dbo.Lider_Cartera (LiderID, CarteraID)
);

-- El nombre de la cartera se obtiene siempre con JOIN a dbo.Cartera por CarteraID;
-- no se almacena denormalizado para evitar inconsistencias si se renombra la cartera.

-- Índice para consultas del informe (filtro por líder/cartera + fecha)
CREATE INDEX IX_MensajesEnviados_LiderCartera_FechaEnvio
    ON dbo.MensajesEnviados (LiderID, CarteraID, FechaEnvio)
    INCLUDE (NumeroTelefono, Canal);

-- Índice para detección de alertas por teléfono + cartera
CREATE INDEX IX_MensajesEnviados_Telefono_Cartera
    ON dbo.MensajesEnviados (NumeroTelefono, CarteraID);

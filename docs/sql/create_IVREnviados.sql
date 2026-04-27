-- ============================================================
-- Tabla IVREnviados
-- Registro de envíos IVR (voz automatizada) a titulares.
-- Espejo de dbo.MensajesEnviados — la carga se alimenta seleccionando
-- Líder + Cartera, por eso referencia a dbo.Lider_Cartera mediante la
-- PK compuesta (LiderID, CarteraID).
--
-- Ejecutar en: DBCallCenter
-- ============================================================

-- Recrear limpia (solo si hay datos de prueba que se pueden perder)
IF OBJECT_ID('dbo.IVREnviados', 'U') IS NOT NULL
    DROP TABLE dbo.IVREnviados;

-- También elimino la tabla legacy IVREnvio si existiera, ya quedó deprecada.
IF OBJECT_ID('dbo.IVREnvio', 'U') IS NOT NULL
    DROP TABLE dbo.IVREnvio;

-- Tabla sin PK surrogate — solo inserción y consulta en agregados.
-- EF Core usa HasNoKey() para esta entidad.
CREATE TABLE dbo.IVREnviados (
    NumeroDocumento BIGINT        NOT NULL,
    NumeroTelefono  NVARCHAR(20)  NOT NULL,
    LiderID         INT           NOT NULL,
    CarteraID       INT           NOT NULL,
    FechaEnvio      DATE          NOT NULL,
    Canal           NVARCHAR(20)  NOT NULL DEFAULT 'IVR',
    FechaCargue     DATETIME2     NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_IVREnviados_LiderCartera
        FOREIGN KEY (LiderID, CarteraID)
        REFERENCES dbo.Lider_Cartera (LiderID, CarteraID)
);

-- El nombre de la cartera se obtiene siempre con JOIN a dbo.Cartera por CarteraID;
-- no se almacena denormalizado para evitar inconsistencias si se renombra la cartera.

-- Índice para consultas del informe (filtro por líder/cartera + fecha)
CREATE INDEX IX_IVREnviados_LiderCartera_FechaEnvio
    ON dbo.IVREnviados (LiderID, CarteraID, FechaEnvio)
    INCLUDE (NumeroTelefono, Canal);

-- Índice para detección de alertas por teléfono + cartera
CREATE INDEX IX_IVREnviados_Telefono_Cartera
    ON dbo.IVREnviados (NumeroTelefono, CarteraID);

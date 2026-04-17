-- =============================================================================
-- Migración manual: Tabla MensajeriaEnviada en DBCallCenter
-- Ejecutar manualmente por el DBA o administrador del sistema.
-- Almacena histórico de mensajería enviada cargada desde Excel.
-- =============================================================================

USE DBCallCenter;
GO

CREATE TABLE MensajeriaEnviada (
    MensajeriaID    INT IDENTITY(1,1) NOT NULL,
    NumeroDocumento BIGINT            NOT NULL,
    NumeroTelefono  VARCHAR(20)       NOT NULL,
    NombreCartera   VARCHAR(100)      NOT NULL,
    CarteraID       INT               NULL,
    FechaEnvio      DATE              NOT NULL,
    FechaCargue     DATETIME2         NOT NULL DEFAULT GETDATE(),

    CONSTRAINT PK_MensajeriaEnviada PRIMARY KEY (MensajeriaID)
);
GO

CREATE INDEX IX_Mensajeria_Telefono ON MensajeriaEnviada(NumeroTelefono);
CREATE INDEX IX_Mensajeria_Cartera  ON MensajeriaEnviada(CarteraID, FechaEnvio);
GO

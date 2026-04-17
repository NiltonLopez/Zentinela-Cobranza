-- =============================================================================
-- Migración manual: Tabla IVREnvio en DBCallCenter
-- Ejecutar manualmente por el DBA o administrador del sistema.
-- Almacena pre-envío (Resultado NULL) y post-envío (Resultado actualizado)
-- de llamadas IVR. El post-envío actualiza por TitularID+Celular+Campana.
-- =============================================================================

USE DBCallCenter;
GO

CREATE TABLE IVREnvio (
    IVRID          INT IDENTITY(1,1) NOT NULL,
    TitularID      BIGINT            NOT NULL,
    Celular        VARCHAR(20)       NOT NULL,
    Campana        VARCHAR(100)      NULL,
    CarteraID      INT               NULL,
    FechaEnvio     DATE              NOT NULL,
    Resultado      VARCHAR(50)       NULL,       -- NULL = pre-envío pendiente
    FechaCargue    DATETIME2         NOT NULL DEFAULT GETDATE(),
    FechaResultado DATETIME2         NULL,

    CONSTRAINT PK_IVREnvio PRIMARY KEY (IVRID)
);
GO

CREATE INDEX IX_IVR_Celular       ON IVREnvio(Celular);
CREATE INDEX IX_IVR_TitularID     ON IVREnvio(TitularID, CarteraID);
CREATE INDEX IX_IVR_Cartera_Fecha ON IVREnvio(CarteraID, FechaEnvio);
GO

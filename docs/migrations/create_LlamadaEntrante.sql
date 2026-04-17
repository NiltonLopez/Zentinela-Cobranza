-- =============================================================================
-- Migración manual: Tabla LlamadaEntrante en DBCallCenter
-- Ejecutar manualmente por el DBA o administrador del sistema.
-- El archivo de origen trae solo celulares; la aplicación resuelve TitularID
-- contra NumeroTelefonoGeneral y evalúa acuerdo de pago en UltimoEstadoTitular.
-- =============================================================================

USE DBCallCenter;
GO

CREATE TABLE LlamadaEntrante (
    LlamadaEntranteID INT IDENTITY(1,1) NOT NULL,
    Celular           VARCHAR(20)       NOT NULL,
    TitularID         BIGINT            NULL,   -- resuelto vs NumeroTelefonoGeneral
    CarteraID         INT               NULL,
    TieneAcuerdo      BIT               NULL,   -- 1 = FechaPromesa vigente en UltimoEstadoTitular
    Semana            INT               NOT NULL,
    Anio              INT               NOT NULL,
    FechaCargue       DATETIME2         NOT NULL DEFAULT GETDATE(),

    CONSTRAINT PK_LlamadaEntrante PRIMARY KEY (LlamadaEntranteID)
);
GO

CREATE INDEX IX_LlamadaEnt_Celular ON LlamadaEntrante(Celular);
CREATE INDEX IX_LlamadaEnt_Semana  ON LlamadaEntrante(Anio, Semana);
GO

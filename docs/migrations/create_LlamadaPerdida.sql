-- =============================================================================
-- Migración manual: Tabla LlamadaPerdida en DBCallCenter
-- Ejecutar manualmente por el DBA o administrador del sistema.
-- Solo almacena titulares vigentes (existen en Titular_Cartera).
-- Los no vigentes se descartan en la capa de aplicación antes de insertar.
-- =============================================================================

USE DBCallCenter;
GO

CREATE TABLE LlamadaPerdida (
    LlamadaPerdidaID INT IDENTITY(1,1) NOT NULL,
    TitularID        BIGINT            NOT NULL,
    Celular          VARCHAR(20)       NOT NULL,
    CarteraID        INT               NOT NULL,
    FechaCargue      DATETIME2         NOT NULL DEFAULT GETDATE(),

    CONSTRAINT PK_LlamadaPerdida PRIMARY KEY (LlamadaPerdidaID)
);
GO

CREATE INDEX IX_LlamadaPerdida_Celular  ON LlamadaPerdida(Celular);
CREATE INDEX IX_LlamadaPerdida_Titular  ON LlamadaPerdida(TitularID, CarteraID);
GO

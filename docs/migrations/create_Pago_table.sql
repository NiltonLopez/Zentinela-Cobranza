-- =============================================================================
-- Migración manual: Tabla Pago en DBCallCenter
-- Ejecutar manualmente por el DBA o administrador del sistema.
-- Esta tabla es propiedad de Zentinela pero reside en DBCallCenter
-- para mantener FK nativas con Titular, Lider y Cartera.
-- =============================================================================

USE DBCallCenter;
GO

CREATE TABLE Pago (
    PagoID           INT IDENTITY(1,1) NOT NULL,
    CedulaTitular    BIGINT            NOT NULL,
    NombreCliente    NVARCHAR(200)     NOT NULL,
    ValorPagado      DECIMAL(18,2)     NOT NULL,
    FechaPago        DATE              NOT NULL,
    Honorarios       DECIMAL(18,2)     NOT NULL,
    LiderID          INT               NOT NULL,
    CarteraID        INT               NOT NULL,
    -- UsuarioRegistro guarda el UserName (ApplicationUser.UserName de DBZentinela)
    -- del usuario que confirmó el cargue. No se crea FK por ser cross-database.
    UsuarioRegistro  NVARCHAR(256)     NOT NULL,
    FechaRegistro    DATETIME2         NOT NULL DEFAULT GETDATE(),

    CONSTRAINT PK_Pago         PRIMARY KEY (PagoID),
    CONSTRAINT FK_Pago_Titular FOREIGN KEY (CedulaTitular) REFERENCES Titular(NumeroDocumento),
    CONSTRAINT FK_Pago_Lider   FOREIGN KEY (LiderID)       REFERENCES Lider(ID),
    CONSTRAINT FK_Pago_Cartera FOREIGN KEY (CarteraID)     REFERENCES Cartera(ID)
);
GO

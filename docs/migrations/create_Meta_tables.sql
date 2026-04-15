-- ============================================================
-- Tablas de metas mensuales — Módulo Informes de Recaudo
-- Base de datos: DBCallCenter
-- Creadas: 2026-04-15
-- ============================================================

USE DBCallCenter;
GO

-- ── Meta por agente por mes ───────────────────────────────────────────────────
CREATE TABLE Meta_Agente_Mes (
    MetaID       int IDENTITY(1,1) NOT NULL,
    AgenteID     bigint            NOT NULL,
    Año          smallint          NOT NULL,
    Mes          tinyint           NOT NULL,  -- 1-12
    MetaRecaudo  decimal(18,2)     NOT NULL,
    CONSTRAINT PK_MetaAgenteMes PRIMARY KEY (MetaID),
    CONSTRAINT FK_MetaAgenteMes_Agente FOREIGN KEY (AgenteID)
        REFERENCES Agente(AgenteID),
    CONSTRAINT UQ_MetaAgenteMes_Periodo UNIQUE (AgenteID, Año, Mes),
    CONSTRAINT CK_MetaAgenteMes_Mes CHECK (Mes BETWEEN 1 AND 12),
    CONSTRAINT CK_MetaAgenteMes_Año CHECK (Año >= 2020)
);
GO

-- ── Meta por líder-cartera por mes ────────────────────────────────────────────
CREATE TABLE Meta_Lider_Cartera_Mes (
    MetaID       int IDENTITY(1,1) NOT NULL,
    LiderID      int               NOT NULL,
    CarteraID    int               NOT NULL,
    Año          smallint          NOT NULL,
    Mes          tinyint           NOT NULL,  -- 1-12
    MetaRecaudo  decimal(18,2)     NOT NULL,
    CONSTRAINT PK_MetaLiderCarteraMes PRIMARY KEY (MetaID),
    CONSTRAINT FK_MetaLiderCarteraMes_LiderCartera FOREIGN KEY (LiderID, CarteraID)
        REFERENCES Lider_Cartera(LiderID, CarteraID),
    CONSTRAINT UQ_MetaLiderCarteraMes_Periodo UNIQUE (LiderID, CarteraID, Año, Mes),
    CONSTRAINT CK_MetaLiderCarteraMes_Mes CHECK (Mes BETWEEN 1 AND 12),
    CONSTRAINT CK_MetaLiderCarteraMes_Año CHECK (Año >= 2020)
);
GO

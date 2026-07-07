-- ============================================================================
-- CONSOLIDACIÓN (2026-07-06): SolicitudVirtualidad es la ÚNICA tabla de
-- solicitudes de virtualidad (IVR / SMS / Correo). Se eliminan las tablas
-- append-only legacy IVREnviados y MensajesEnviados — ambas VACÍAS (0 filas),
-- sin pérdida de datos. Todo el código de lectura (Informes de Virtualidad,
-- Pagos vs Impacto, Gestión de Carteras) se reapuntó a esta tabla.
-- NOTA: EmailEnviados (935k filas) NO se toca — es histórico real de Correo,
-- pendiente de decidir su integración.
-- ============================================================================

IF OBJECT_ID('dbo.IVREnviados', 'U') IS NOT NULL
    DROP TABLE dbo.IVREnviados;
GO

IF OBJECT_ID('dbo.MensajesEnviados', 'U') IS NOT NULL
    DROP TABLE dbo.MensajesEnviados;
GO

-- ============================================================================
-- Tabla: dbo.SolicitudVirtualidad  (DBCallCenter)
-- Histórico consolidado de solicitudes de virtualidad (IVR / SMS / Correo).
--
-- Modelo de una sola tabla con discriminador Canal y campo Estado ACTUALIZABLE:
--   - Fase Solicitud: se inserta con Estado = NULL (aún sin resultado).
--   - Fase Resultado: se ACTUALIZA Estado + FechaResultado por fila (vía PK ID).
--
-- El mapeo a Cartera/Líder se resuelve por NOMBRE en el archivo y se ancla al
-- par (LiderID, CarteraID) mediante FK a Lider_Cartera (mismo criterio ADR-014:
-- la trazabilidad histórica se ancla al par líder-cartera, que usa soft-delete).
-- ============================================================================

IF OBJECT_ID('dbo.SolicitudVirtualidad', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudVirtualidad
    (
        ID              BIGINT       IDENTITY(1,1) NOT NULL,
        Canal           VARCHAR(10)  NOT NULL,   -- IVR | SMS | Correo
        NumeroDocumento BIGINT       NOT NULL,
        Destino         VARCHAR(255) NOT NULL,   -- teléfono (IVR/SMS) o email (Correo)
        CarteraID       INT          NOT NULL,
        LiderID         INT          NOT NULL,
        FechaEnvio      DATE         NOT NULL,
        Estado          VARCHAR(50)  NULL,       -- NULL = solicitud sin resultado
        FechaResultado  DATETIME2    NULL,
        FechaCargue     DATETIME2    NOT NULL
            CONSTRAINT DF_SolicitudVirtualidad_FechaCargue DEFAULT (GETDATE()),

        CONSTRAINT PK_SolicitudVirtualidad PRIMARY KEY CLUSTERED (ID),

        CONSTRAINT CK_SolicitudVirtualidad_Canal
            CHECK (Canal IN ('IVR', 'SMS', 'Correo')),

        CONSTRAINT FK_SolicitudVirtualidad_LiderCartera
            FOREIGN KEY (LiderID, CarteraID)
            REFERENCES dbo.Lider_Cartera (LiderID, CarteraID)
            ON DELETE NO ACTION
    );

    -- Alertas de saturación (>N envíos al mismo Destino por canal) y filtros de informe.
    CREATE NONCLUSTERED INDEX IX_SolicitudVirtualidad_Canal_Cartera_Fecha
        ON dbo.SolicitudVirtualidad (Canal, CarteraID, FechaEnvio);

    CREATE NONCLUSTERED INDEX IX_SolicitudVirtualidad_Canal_Destino
        ON dbo.SolicitudVirtualidad (Canal, Destino);
END
GO

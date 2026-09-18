-- ============================================================================
-- Creador de Virtualidad (SMS) + Mensajes Autorizados — DBCallCenter (ADR-037)
--
--   Tres tablas nuevas:
--     MensajeAutorizado          catálogo cerrado de textos SMS (los escriben abogados).
--     AgenteContactoVirtualidad  número corporativo + link de WhatsApp por asesor.
--     EstrategiaSms              auditoría de cada archivo generado (quién, qué, cuántos).
--
--   NO se toca HistoricoVirtualidad: el envío real lo registra el área de virtualidad.
--   Las entidades EF llevan ExcludeFromMigrations(): sin este script la API falla en
--   runtime con "El nombre de objeto ... no es válido". Aplicar ANTES del deploy.
--
--   Idempotente. Fecha: 2026-09-18.
-- ============================================================================

IF OBJECT_ID('dbo.MensajeAutorizado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MensajeAutorizado
    (
        ID                   INT           IDENTITY(1,1) NOT NULL,
        Titulo               VARCHAR(100)  NOT NULL,
        -- Texto con placeholders {NOMBRE} {CELULAR_ASESOR} {LINK_ASESOR}. 320 deja
        -- margen para escribir; el límite real (160 con variables) lo valida la API.
        Texto                VARCHAR(320)  NOT NULL,
        Activo               BIT           NOT NULL CONSTRAINT DF_MensajeAutorizado_Activo DEFAULT (1),
        FechaCreacion        DATETIME2     NOT NULL CONSTRAINT DF_MensajeAutorizado_FechaCreacion DEFAULT (GETDATE()),
        UsuarioCreacion      VARCHAR(100)  NOT NULL,
        FechaActualizacion   DATETIME2     NOT NULL CONSTRAINT DF_MensajeAutorizado_FechaAct DEFAULT (GETDATE()),
        UsuarioActualizacion VARCHAR(100)  NOT NULL,
        CONSTRAINT PK_MensajeAutorizado PRIMARY KEY CLUSTERED (ID),
        -- El celular y el link del asesor son obligatorios en todo mensaje: sin ellos el
        -- titular no tiene a quién responder. Se valida también en la API; el CHECK es la
        -- última red por si alguien inserta por SQL.
        CONSTRAINT CK_MensajeAutorizado_Placeholders
            CHECK (Texto LIKE '%{CELULAR_ASESOR}%' AND Texto LIKE '%{LINK_ASESOR}%')
    );
    CREATE NONCLUSTERED INDEX IX_MensajeAutorizado_Activo
        ON dbo.MensajeAutorizado (Activo) INCLUDE (Titulo);
END
GO

IF OBJECT_ID('dbo.AgenteContactoVirtualidad', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AgenteContactoVirtualidad
    (
        AgenteID             BIGINT        NOT NULL,
        NumeroCorporativo    VARCHAR(10)   NULL,
        LinkWhatsapp         VARCHAR(100)  NULL,
        FechaActualizacion   DATETIME2     NOT NULL CONSTRAINT DF_AgenteContactoVirt_FechaAct DEFAULT (GETDATE()),
        UsuarioActualizacion VARCHAR(100)  NOT NULL,
        CONSTRAINT PK_AgenteContactoVirtualidad PRIMARY KEY CLUSTERED (AgenteID),
        CONSTRAINT FK_AgenteContactoVirtualidad_Agente
            FOREIGN KEY (AgenteID) REFERENCES dbo.Agente (AgenteID) ON DELETE CASCADE,
        CONSTRAINT CK_AgenteContactoVirt_Numero
            CHECK (NumeroCorporativo IS NULL
                   OR (LEN(NumeroCorporativo) = 10
                       AND NumeroCorporativo LIKE '3[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'))
    );
END
GO

IF OBJECT_ID('dbo.EstrategiaSms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EstrategiaSms
    (
        ID                  BIGINT        IDENTITY(1,1) NOT NULL,
        LiderID             INT           NOT NULL,
        UsuarioGenero       VARCHAR(100)  NOT NULL,
        FechaGeneracion     DATETIME2     NOT NULL CONSTRAINT DF_EstrategiaSms_Fecha DEFAULT (GETDATE()),
        MensajeAutorizadoID INT           NOT NULL,
        TotalLeidos         INT           NOT NULL,
        TotalGenerados      INT           NOT NULL,
        ExcluidosPerfil     INT           NOT NULL,   -- archivo + BD
        ExcluidosListaNegra INT           NOT NULL,   -- tabla + archivo
        ExcluidosLongitud   INT           NOT NULL,   -- superaban 160 con el peor link
        ExcluidosOtros      INT           NOT NULL,   -- cédula/teléfono inválido, fijo, nombre vacío, duplicado
        HuellaArchivo       CHAR(64)      NOT NULL,   -- SHA-256 hex del archivo de entrada
        NombreArchivo       VARCHAR(255)  NOT NULL,
        CONSTRAINT PK_EstrategiaSms PRIMARY KEY CLUSTERED (ID),
        CONSTRAINT FK_EstrategiaSms_Lider
            FOREIGN KEY (LiderID) REFERENCES dbo.Lider (ID),
        CONSTRAINT FK_EstrategiaSms_MensajeAutorizado
            FOREIGN KEY (MensajeAutorizadoID) REFERENCES dbo.MensajeAutorizado (ID)
    );
    CREATE NONCLUSTERED INDEX IX_EstrategiaSms_Lider_Fecha
        ON dbo.EstrategiaSms (LiderID, FechaGeneracion DESC);
    -- Detectar que el mismo archivo se generó más de una vez.
    CREATE NONCLUSTERED INDEX IX_EstrategiaSms_Huella
        ON dbo.EstrategiaSms (HuellaArchivo);
END
GO

-- Verificación
SELECT name, create_date
  FROM sys.tables
 WHERE name IN ('MensajeAutorizado', 'AgenteContactoVirtualidad', 'EstrategiaSms')
 ORDER BY name;
GO

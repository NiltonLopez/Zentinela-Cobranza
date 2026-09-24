-- ============================================================
-- dbo.SOSAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2025-09-16 12:48:16
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.SOSAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.SOSAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SOSAuxiliar] (
        [TitularID]     bigint         NOT NULL,
        [NitEmpresa]    bigint         NULL,
        [RazonSocial]   varchar(100)   NULL,
        [Telefono]      varchar(10)    NULL,
        [Dirección]     varchar(150)   NULL,
        [EstadoEnSos]   bit            NULL,
        [FechaConsulta] datetime       NULL,
        [Nombre]        varchar(80)    NULL,
        [CarteraID]     int            NULL,
        [LiderID]       int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

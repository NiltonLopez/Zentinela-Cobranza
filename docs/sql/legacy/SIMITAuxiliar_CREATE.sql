-- ============================================================
-- dbo.SIMITAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2025-07-26 12:16:18
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.SIMITAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.SIMITAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SIMITAuxiliar] (
        [TitularID]     bigint         NOT NULL,
        [Placa1]        varchar(10)    NULL,
        [Placa2]        varchar(10)    NULL,
        [Placa3]        varchar(10)    NULL,
        [Placa4]        varchar(10)    NULL,
        [Placa5]        varchar(10)    NULL,
        [Placa6]        varchar(10)    NULL,
        [Placa7]        varchar(10)    NULL,
        [Placa8]        varchar(10)    NULL,
        [Placa9]        varchar(10)    NULL,
        [Placa10]       varchar(10)    NULL,
        [Placa11]       varchar(10)    NULL,
        [Placa12]       varchar(10)    NULL,
        [Placa13]       varchar(10)    NULL,
        [Placa14]       varchar(10)    NULL,
        [Placa15]       nchar(10)      NULL,
        [EstadoEnSIMIT] bit            NULL,
        [FechaConsulta] datetime       NULL,
        [Nombre]        varchar(50)    NULL,
        [CarteraID]     int            NULL,
        [LiderID]       int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

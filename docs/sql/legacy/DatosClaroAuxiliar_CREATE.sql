-- ============================================================
-- dbo.DatosClaroAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2026-03-19 18:09:47
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.DatosClaroAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.DatosClaroAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[DatosClaroAuxiliar] (
        [TitularID]      bigint         NULL,
        [NumeroTelefono] varchar(10)    NULL,
        [Email]          varchar(150)   NULL,
        [FechaInsercion] date           NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

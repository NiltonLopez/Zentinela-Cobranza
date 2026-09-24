-- ============================================================
-- dbo.TigoUneAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2026-08-04 17:48:25
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.TigoUneAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.TigoUneAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[TigoUneAuxiliar] (
        [TitularID]       bigint         NULL,
        [NumeroTelefono]  varchar(10)    NULL,
        [Email]           varchar(255)   NULL,
        [EstadoEnTigoUne] bit            NULL,
        [FechaConsulta]   datetime       NULL,
        [Nombre]          varchar(70)    NULL,
        [CarteraID]       int            NULL,
        [LiderID]         int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

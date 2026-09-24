-- ============================================================
-- dbo.CompensadosAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2026-02-18 16:30:05
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.CompensadosAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.CompensadosAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[CompensadosAuxiliar] (
        [TitularID]               bigint         NULL,
        [EPS]                     varchar(50)    NULL,
        [UltimoPeriodoCompensado] varchar(50)    NULL,
        [TipoAfiliado]            varchar(50)    NULL,
        [Estado]                  int            NULL,
        [FechaConsulta]           datetime       NULL,
        [Nombre]                  varchar(50)    NULL,
        [CarteraID]               int            NULL,
        [FechaInsercion]          datetime       NULL,
        [LiderID]                 int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

-- ============================================================
-- dbo.PositivaEmpresasAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2025-10-23 15:37:38
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.PositivaEmpresasAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.PositivaEmpresasAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[PositivaEmpresasAuxiliar] (
        [TitularID]               bigint         NULL,
        [NIT]                     bigint         NULL,
        [Empresa]                 varchar(230)   NULL,
        [EstadoAfiliacion]        varchar(30)    NULL,
        [EstadoEnPositivaEmpresa] bit            NULL,
        [FechaConsulta]           datetime       NULL,
        [FechaInsercion]          datetime       NULL CONSTRAINT [DF_PositivaEmpresasAuxiliar_FechaInsercion] DEFAULT (getdate()),
        [Nombre]                  varchar(70)    NULL,
        [CarteraID]               int            NULL,
        [LiderID]                 int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

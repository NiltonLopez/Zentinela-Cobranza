-- ============================================================
-- dbo.SuraAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2026-09-02 16:04:09
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.SuraAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.SuraAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SuraAuxiliar] (
        [TitularID]           bigint         NULL,
        [NumeroCelular]       varchar(10)    NULL,
        [Email]               varchar(255)   NULL,
        [NIT]                 bigint         NULL,
        [Empresa]             varchar(100)   NULL,
        [TipoAfiliado]        varchar(50)    NULL,
        [EstadoAfiliacion]    varchar(50)    NULL,
        [UltimaFechaConsulta] datetime       NULL,
        [EstadoEnSura]        bit            NULL,
        [FechaConsulta]       datetime       NULL,
        [Nombre]              varchar(80)    NULL,
        [CarteraID]           int            NULL,
        [LiderID]             int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

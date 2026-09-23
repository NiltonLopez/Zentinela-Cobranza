-- ============================================================
-- dbo.MutualSerAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2026-06-03 12:34:14
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.MutualSerAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.MutualSerAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[MutualSerAuxiliar] (
        [TitularID]              bigint         NULL,
        [NumeroTelefonoCelular]  varchar(10)    NULL,
        [NumeroTelefonoCelular2] varchar(50)    NULL,
        [NumeroTelefonoFijo]     varchar(10)    NULL,
        [Email]                  varchar(255)   NULL,
        [Regimen]                varchar(30)    NULL,
        [TipoAfiliado]           varchar(50)    NULL,
        [Departamento]           varchar(50)    NULL,
        [Municipio]              varchar(50)    NULL,
        [EstadoEnMutualSer]      bit            NULL,
        [Nombre]                 varchar(80)    NULL,
        [FechaConsulta]          date           NULL,
        [CarteraID]              int            NULL,
        [LiderID]                int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

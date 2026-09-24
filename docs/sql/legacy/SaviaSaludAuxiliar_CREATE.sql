-- ============================================================
-- dbo.SaviaSaludAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2026-08-04 14:10:20
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.SaviaSaludAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.SaviaSaludAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SaviaSaludAuxiliar] (
        [TitularID]        bigint         NULL,
        [FechaNacimiento]  datetime       NULL,
        [TipoAfiliado]     varchar(70)    NULL,
        [EstadoAfiliacion] varchar(70)    NULL,
        [Regimen]          varchar(70)    NULL,
        [Email]            varchar(100)   NULL,
        [NumeroTelefono]   varchar(10)    NULL,
        [NumeroCelular]    varchar(30)    NULL,
        [Nombre]           varchar(70)    NULL,
        [Estado]           bit            NULL,
        [CarteraID]        int            NULL,
        [LiderID]          int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

-- ============================================================
-- dbo.CoosaludAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2026-08-04 14:22:28
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.CoosaludAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.CoosaludAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[CoosaludAuxiliar] (
        [TitularID]          bigint         NULL,
        [NumeroTelefono]     varchar(10)    NULL,
        [Email]              varchar(255)   NULL,
        [MunicipioOrigen]    varchar(50)    NULL,
        [DepartamentoOrigen] varchar(50)    NULL,
        [Regimen]            varchar(50)    NULL,
        [TipoAfiliado]       varchar(50)    NULL,
        [EstadoAfiliacion]   varchar(50)    NULL,
        [NivelSisben]        varchar(50)    NULL,
        [FechaNacimiento]    datetime       NULL,
        [EstadoEnCoosalud]   bit            NULL,
        [FechaConsulta]      datetime       NULL,
        [Nombre]             varchar(50)    NULL,
        [CarteraID]          int            NULL,
        [LiderID]            int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

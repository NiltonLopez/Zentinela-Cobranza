-- ============================================================
-- dbo.FosygaAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2025-06-27 14:26:08
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.FosygaAuxiliar = 18598
-- ATENCION: la tabla tenia filas al respaldar (ver PARIDAD-otras.md, seccion
-- 'Auxiliar con datos'). Este archivo respalda SOLO la estructura, no los datos.
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.FosygaAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[FosygaAuxiliar] (
        [TitularID]                   bigint         NULL,
        [Estado]                      varchar(30)    NULL,
        [EPS]                         varchar(200)   NULL,
        [Regimen]                     varchar(30)    NULL,
        [FechaAfiliacionEfectiva]     date           NULL,
        [FechaFinalizacionAfiliacion] date           NULL,
        [TipoAfiliado]                varchar(50)    NULL,
        [Departamento]                varchar(70)    NULL,
        [Municipio]                   varchar(50)    NULL,
        [EstadoEnFosyga]              bit            NULL,
        [FechaConsulta]               datetime       NULL,
        [Nombre]                      varchar(60)    NULL,
        [CarteraID]                   int            NULL,
        [FechaInsercion]              datetime       NULL CONSTRAINT [DF_FosygaAuxiliar_FechaInsercion] DEFAULT (getdate()),
        [LiderID]                     int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

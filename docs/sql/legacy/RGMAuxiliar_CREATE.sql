-- ============================================================
-- dbo.RGMAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2025-07-01 13:06:22
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.RGMAuxiliar = 30144
-- ATENCION: la tabla tenia filas al respaldar (ver PARIDAD-otras.md, seccion
-- 'Auxiliar con datos'). Este archivo respalda SOLO la estructura, no los datos.
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.RGMAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[RGMAuxiliar] (
        [TitularID]      bigint         NULL,
        [Marca]          varchar(50)    NULL,
        [Modelo]         varchar(50)    NULL,
        [Placa]          varchar(6)     NULL,
        [EstadoEnRGM]    bit            NULL,
        [Nombre]         varchar(70)    NULL,
        [CarteraID]      int            NULL,
        [FechaConsulta]  datetime       NULL,
        [FechaInsercion] datetime       NULL CONSTRAINT [DF_RGMAuxiliar_FechaInsercion] DEFAULT (getdate()),
        [LiderID]        int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

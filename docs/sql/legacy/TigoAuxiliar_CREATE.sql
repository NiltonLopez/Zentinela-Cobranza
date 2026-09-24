-- ============================================================
-- dbo.TigoAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24.
-- Tabla creada en la BD: 2025-03-29 09:37:12 (modificada 2026-04-13 11:55:42)
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.TigoAuxiliar = 661072
-- ATENCION: la tabla tenia filas al respaldar: residuo de las cargas de
-- febrero-marzo de 2025, ya contenido en dbo.Tigo (ver PARIDAD-otras.md,
-- seccion 'Auxiliar con datos'). Este archivo respalda SOLO la estructura.
-- No hay sp_ProcesarTigoAuxiliar: dbo.Tigo se lleno a mano desde esta tabla.
-- Sin columna Nombre: el cargue nuevo de Tigo SI la exige (FK a Titular).
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin defaults, FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.TigoAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[TigoAuxiliar] (
        [TitularID]      bigint         NULL,
        [NumeroTelefono] varchar(10)    NULL,
        [Email]          varchar(255)   NULL,
        [EstadoEnTigo]   bit            NULL,
        [FechaConsulta]  datetime       NULL,
        [CarteraID]      int            NULL,
        [LiderID]        int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

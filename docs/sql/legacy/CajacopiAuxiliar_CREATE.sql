-- ============================================================
-- dbo.CajacopiAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2025-06-09 14:04:56
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.CajacopiAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.CajacopiAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[CajacopiAuxiliar] (
        [TitularID]             bigint         NULL,
        [NumeroTelefonoCelular] varchar(10)    NULL,
        [NumeroTelefonoFijo]    varchar(10)    NULL,
        [Email]                 varchar(100)   NULL,
        [Departamento]          varchar(50)    NULL,
        [Municipio]             varchar(50)    NULL,
        [FechaNacimiento]       datetime       NULL,
        [EstadoEnCajacopi]      bit            NULL,
        [FechaConsulta]         datetime       NULL CONSTRAINT [DF_CajacopiAuxiliar_FechaConsulta] DEFAULT (getdate()),
        [Nombre]                varchar(80)    NULL,
        [CarteraID]             int            NULL,
        [LiderID]               int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

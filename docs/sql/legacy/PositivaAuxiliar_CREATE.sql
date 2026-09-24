-- ============================================================
-- dbo.PositivaAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2025-07-14 13:31:33
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.PositivaAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.PositivaAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[PositivaAuxiliar] (
        [TitularID]             bigint         NULL,
        [NumeroTelefonoCelular] varchar(10)    NULL,
        [NumeroTelefonoFijo]    varchar(30)    NULL,
        [Email]                 varchar(100)   NULL,
        [FechaNacimiento]       datetime       NULL,
        [Departamento]          varchar(50)    NULL,
        [Municipio]             varchar(50)    NULL,
        [EstadoEnPositiva]      bit            NULL,
        [FechaConsulta]         datetime       NULL,
        [Nombre]                varchar(70)    NULL,
        [CarteraID]             int            NULL,
        [LiderID]               int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

-- ============================================================
-- dbo.CamaraAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2025-12-06 13:44:47
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.CamaraAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.CamaraAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[CamaraAuxiliar] (
        [TitularID]      bigint         NULL,
        [NumeroTelefono] varchar(10)    NULL,
        [Email]          varchar(255)   NULL,
        [Municipio]      varchar(50)    NULL,
        [Departamento]   varchar(70)    NULL,
        [EstadoEnCamara] bit            NULL,
        [FechaConsulta]  datetime       NULL,
        [Nombre]         varchar(80)    NULL,
        [CarteraID]      int            NULL,
        [LiderID]        int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

-- ============================================================
-- dbo.CamaraEmpresaAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:38.
-- Tabla creada en la BD: 2025-07-31 16:47:52
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.CamaraEmpresaAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.CamaraEmpresaAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[CamaraEmpresaAuxiliar] (
        [NitEmpresa]     bigint         NULL,
        [NumeroTelefono] varchar(10)    NULL,
        [Email]          varchar(255)   NULL,
        [Departamento]   varchar(60)    NULL,
        [EstadoEnCamara] bit            NULL,
        [FechaConsulta]  datetime       NULL,
        [NombreEmpresa]  varchar(120)   NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

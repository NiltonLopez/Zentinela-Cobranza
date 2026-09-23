-- ============================================================
-- dbo.NuevaEpsAuxiliar  (respaldo legacy, ADR-039)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-23 22:38.
-- Tabla creada en la BD: 2026-06-02 13:32:06
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.NuevaEpsAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers.
-- ============================================================
IF OBJECT_ID('dbo.NuevaEpsAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[NuevaEpsAuxiliar] (
        [TitularID]        varchar(10)    NULL,
        [NumeroTelefono]   varchar(10)    NULL,
        [EstadoAfiliacion] varchar(30)    NULL,
        [TipoAfiliado]     varchar(50)    NULL,
        [FechaNacimiento]  date           NULL,
        [Departamento]     varchar(50)    NULL,
        [Municipio]        varchar(50)    NULL,
        [EstadoEnNuevaEps] bit            NULL,
        [Nombre]           varchar(70)    NULL,
        [CarteraID]        int            NULL,
        [LiderID]          int            NULL,
        [FechaConsulta]    date           NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

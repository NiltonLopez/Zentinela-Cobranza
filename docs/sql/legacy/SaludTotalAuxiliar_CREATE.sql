-- ============================================================
-- dbo.SaludTotalAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2026-01-28 14:40:23
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.SaludTotalAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.SaludTotalAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[SaludTotalAuxiliar] (
        [TitularID]          bigint         NULL,
        [NumeroTelefono]     varchar(10)    NULL,
        [Email]              varchar(255)   NULL,
        [FechaNacimiento]    date           NULL,
        [EstadoAfiliacion]   varchar(30)    NULL,
        [TipoAfiliado]       varchar(50)    NULL,
        [Ciudad]             varchar(50)    NULL,
        [NIT]                bigint         NULL,
        [Empresa]            varchar(100)   NULL,
        [Cargo]              varchar(60)    NULL,
        [UltimaFechaPago]    datetime       NULL,
        [IngresoBase]        varchar(20)    NULL,
        [EstadoEnSaludTotal] bit            NULL,
        [FechaConsulta]      datetime       NULL,
        [Nombre]             varchar(70)    NULL,
        [CarteraID]          int            NULL,
        [LiderID]            int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

-- ============================================================
-- dbo.MiVacunaAuxiliar  (respaldo legacy, ADR-039, fase 2 grupo salud)
-- Generado desde sys.columns / sys.types / sys.default_constraints /
-- sys.indexes de DBCallCenter el 2026-09-24 16:32.
-- Tabla creada en la BD: 2026-05-22 10:25:39
-- Filas al momento del respaldo: SELECT COUNT(*) FROM dbo.MiVacunaAuxiliar = 0
-- Columnas de texto: collation Modern_Spanish_CI_AS (la default de la BD,
-- por eso no se escribe COLLATE). Filegroup PRIMARY, sin compresion.
-- Sin FKs, CHECKs ni triggers. Sin defaults.
-- ============================================================
IF OBJECT_ID('dbo.MiVacunaAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.[MiVacunaAuxiliar] (
        [TitularID]        bigint         NULL,
        [FechaExpedicion]  date           NULL,
        [Celular]          varchar(10)    NULL,
        [Email]            varchar(255)   NULL,
        [EstadoEnMiVacuna] bit            NULL,
        [Nombre]           varchar(70)    NULL,
        [FechaConsulta]    date           NULL,
        [CarteraID]        int            NULL,
        [LiderID]          int            NULL
    );
END;
GO

-- Indices: ninguno. La tabla es un HEAP (sin PK, sin indice clustered ni nonclustered).

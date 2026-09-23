-- Banco de pruebas del rediseño del ETL. Reproduce la forma real de las tablas
-- de DBCallCenter (tipos exactos), sin particionamiento ni columnstore: los
-- procedimientos no dependen de eso.
IF DB_ID('EtlHarness') IS NOT NULL
BEGIN
    ALTER DATABASE EtlHarness SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE EtlHarness;
END
GO
CREATE DATABASE EtlHarness;
GO
USE EtlHarness;
GO

CREATE TABLE dbo.UltimoEstadoTitular (
    TitularID BIGINT NOT NULL, CarteraID INT NOT NULL, AgenteID INT NULL,
    UltimoPerfil VARCHAR(50) NULL, MejorPerfil VARCHAR(50) NULL,
    SaldoTotal MONEY NULL, NumeroMarcado VARCHAR(10) NULL, Rotacion INT NULL,
    FechaUltimaGestion DATE NULL, FechaPromesa DATE NULL, ValorPromesa MONEY NULL,
    MotivoNoPago VARCHAR(100) NULL, Gestion NVARCHAR(1000) NULL,
    FechaActualizacion DATETIME NOT NULL,
    CONSTRAINT PK_UltimoEstadoTitular PRIMARY KEY CLUSTERED (TitularID, CarteraID));

CREATE TABLE dbo.Staging_UltimoEstadoTitular (
    TitularID BIGINT NOT NULL, CarteraID INT NOT NULL, AgenteID INT NULL,
    UltimoPerfil VARCHAR(50) NULL, MejorPerfil VARCHAR(50) NULL,
    SaldoTotal MONEY NULL, NumeroMarcado VARCHAR(10) NULL, Rotacion INT NULL,
    FechaUltimaGestion DATE NULL, FechaPromesa DATE NULL, ValorPromesa MONEY NULL,
    MotivoNoPago VARCHAR(100) NULL, Gestion NVARCHAR(1000) NULL,
    FechaActualizacion DATETIME NOT NULL);

CREATE TABLE dbo.HistoricoEstadoTitular (
    TitularID BIGINT NOT NULL, CarteraID INT NOT NULL, AgenteID INT NULL,
    UltimoPerfil VARCHAR(50) NULL, MejorPerfil VARCHAR(50) NULL,
    SaldoTotal DECIMAL(18,0) NULL, NumeroMarcado VARCHAR(10) NULL, Rotacion INT NULL,
    FechaUltimaGestion DATE NULL, FechaPromesa DATE NULL, ValorPromesa DECIMAL(18,0) NULL,
    MotivoNoPago VARCHAR(100) NULL, Gestion NVARCHAR(1000) NULL,
    FechaActualizacion DATETIME NOT NULL);
GO

-- Estado de partida: 22 carteras cargadas por el ETL (1..22) + la cartera 99,
-- que representa lo que el Cargue de Reportes de Zentinela escribió por su
-- cuenta y que el ETL nocturno NO debe pisar.
DECLARE @i INT = 1;
WHILE @i <= 22
BEGIN
    INSERT INTO dbo.UltimoEstadoTitular
        (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion,
         FechaUltimaGestion, FechaActualizacion)
    SELECT TOP (100) 1000000 + @i * 1000 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
           @i, 7, 'CONTACTO', 500000, 1, '2026-09-11', '2026-09-11T13:00:00'
    FROM sys.all_objects;
    SET @i += 1;
END

INSERT INTO dbo.UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion,
     FechaUltimaGestion, FechaActualizacion)
VALUES (999001, 99, 7, 'CARGUE MANUAL', 123456, 1, '2026-09-20', '2026-09-20T09:00:00');

-- Histórico con las mismas 22 carteras, para que la referencia de la primera
-- corrida (sin auditoría previa) sea 22.
INSERT INTO dbo.HistoricoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion,
     FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion,
       FechaUltimaGestion, FechaActualizacion
FROM dbo.UltimoEstadoTitular WHERE CarteraID <= 22;
GO

PRINT 'Setup listo.';
SELECT COUNT(*) AS filas_ultimo, COUNT(DISTINCT CarteraID) AS carteras_ultimo
FROM dbo.UltimoEstadoTitular;
GO

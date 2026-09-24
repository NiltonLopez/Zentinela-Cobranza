-- ============================================================================
-- Banco de pruebas del CARGADOR (modo publicar) — ADR-039
--
-- Base desechable en LocalDB con las tablas que toca el cargador y las carteras
-- REALES del subconjunto de prueba (IDs, nombres y NombreArchivo de produccion; no
-- son datos personales). Agente queda vacia: la busqueda de asesor da NULL, que es
-- justamente lo que hace el paquete cuando no encuentra (IgnoreFailure).
--
-- Despues de esto se aplican las dos migraciones (rediseno + cargador) cambiando la
-- base de destino, y se corre cargar.ps1 -Modo publicar contra esta base.
-- ============================================================================
IF DB_ID('CargadorHarness') IS NOT NULL
BEGIN
    ALTER DATABASE CargadorHarness SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CargadorHarness;
END
GO
CREATE DATABASE CargadorHarness;
GO
USE CargadorHarness;
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

CREATE TABLE dbo.Cartera (
    ID INT NOT NULL PRIMARY KEY, NombreCartera VARCHAR(100) NULL,
    Activa BIT NOT NULL, NombreArchivo NVARCHAR(50) NULL);

CREATE TABLE dbo.Agente (AgenteID BIGINT NOT NULL PRIMARY KEY, UsuarioControlNext VARCHAR(100) NULL);
GO

-- Carteras reales del subconjunto (Delmar_prej se reparte en ELITE y CONTACT por
-- Tipo_Cartera). La 2 esta INACTIVA a proposito: si alguna fila trae ese tipo, tiene
-- que quedar rechazada como 'tipo-cartera-inactiva' en vez de perderse.
INSERT INTO dbo.Cartera (ID, NombreCartera, Activa, NombreArchivo) VALUES
    (9,  'DELMAR ELITE',         1, N'Delmar_prej'),
    (10, 'DELMAR CONTACT',       1, N'Delmar_prej'),
    (14, 'CASA HOGAR',           1, N'Rio_Verde'),
    (25, 'PRIMA Y NUEVAGARANTIA',   1, N'Nova_Garant'),
    (26, 'LUMA',                 1, N'Paradas_Credito'),
    (2,  'ALIARCOM CASTIGADA',   0, N'grupo_ceiba');
GO
PRINT 'CargadorHarness listo.';

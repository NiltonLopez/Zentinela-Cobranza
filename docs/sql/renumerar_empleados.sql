-- ============================================================
-- renumerar_empleados.sql
-- Renumera Empleado.ID > 441 en orden por ID original ascendente,
-- asignando IDs consecutivos 442, 443, ...
-- Debe ejecutarse DESPUÉS de seed_ejemplos_rollback.sql.
--
-- Pre-condiciones verificadas el 2026-05-05:
--   • IDSupervisor: 0 referencias hacia el rango > 441
--   • Usuario.EmpleadoID: 0 filas dependientes
--   • TurnoAsignacion.EmpleadoID: 0 filas dependientes
--   • DBZentinela / DBCallCenter no usan EmpleadoID (solo NumeroDocumento)
-- ============================================================

USE DBRecursosHumanos;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Capturar filas a renumerar con su nuevo ID
DECLARE @temp TABLE (
    NewID            INT,
    NumeroDocumento  BIGINT,
    TipoDocumento    INT,
    Nombre           VARCHAR(200),
    Apellidos        VARCHAR(200),
    FechaNacimiento  DATETIME,
    Sexo             CHAR(1),
    RH               CHAR(3),
    Direccion        VARCHAR(300),
    Ciudad           VARCHAR(100),
    NumeroTelefono   VARCHAR(50),
    Email            VARCHAR(200),
    FechaIngreso     DATETIME,
    FechaRetiro      DATETIME,
    CargoID          INT,
    EmpresaID        INT,
    SalarioBase      DECIMAL(18,2),
    EPS              VARCHAR(100),
    AFP              VARCHAR(100),
    IDSupervisor     INT,
    EstadoEmpleado   BIT,
    Piso             INT
);

INSERT INTO @temp
SELECT
    441 + ROW_NUMBER() OVER (ORDER BY ID ASC) AS NewID,
    NumeroDocumento, TipoDocumento, Nombre, Apellidos, FechaNacimiento,
    Sexo, RH, Direccion, Ciudad, NumeroTelefono, Email,
    FechaIngreso, FechaRetiro, CargoID, EmpresaID, SalarioBase,
    EPS, AFP, IDSupervisor, EstadoEmpleado, Piso
FROM dbo.Empleado
WHERE ID > 441;

DECLARE @maxNew INT = (SELECT MAX(NewID) FROM @temp);
DECLARE @count  INT = (SELECT COUNT(*)   FROM @temp);
PRINT CONCAT('Filas a renumerar: ', @count, '   Nuevo MAX(ID): ', @maxNew);

-- 2. Borrar las filas viejas (NumeroDocumento es UNIQUE — no podrían coexistir)
DELETE FROM dbo.Empleado WHERE ID > 441;

-- 3. Insertar de nuevo con IDs consecutivos
SET IDENTITY_INSERT dbo.Empleado ON;

INSERT INTO dbo.Empleado (
    ID, NumeroDocumento, TipoDocumento, Nombre, Apellidos, FechaNacimiento,
    Sexo, RH, Direccion, Ciudad, NumeroTelefono, Email,
    FechaIngreso, FechaRetiro, CargoID, EmpresaID, SalarioBase,
    EPS, AFP, IDSupervisor, EstadoEmpleado, Piso
)
SELECT
    NewID, NumeroDocumento, TipoDocumento, Nombre, Apellidos, FechaNacimiento,
    Sexo, RH, Direccion, Ciudad, NumeroTelefono, Email,
    FechaIngreso, FechaRetiro, CargoID, EmpresaID, SalarioBase,
    EPS, AFP, IDSupervisor, EstadoEmpleado, Piso
FROM @temp
ORDER BY NewID;

SET IDENTITY_INSERT dbo.Empleado OFF;

COMMIT TRANSACTION;
GO

-- 4. Reseed para que el próximo INSERT continúe la secuencia
DECLARE @max INT = (SELECT ISNULL(MAX(ID), 441) FROM dbo.Empleado);
DBCC CHECKIDENT('dbo.Empleado', RESEED, @max);
GO

-- 5. Verificación
SELECT 'Empleados con ID > 441 tras renumerado' AS Diag;
SELECT ID, NumeroDocumento FROM dbo.Empleado WHERE ID > 441 ORDER BY ID;
GO

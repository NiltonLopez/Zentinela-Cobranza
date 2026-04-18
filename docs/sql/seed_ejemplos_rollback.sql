-- ============================================================
-- seed_ejemplos_rollback.sql — Limpieza de datos de prueba
-- ============================================================
-- Elimina exactamente los registros creados por seed_ejemplos.sql
-- y seed_ejemplos_pagos.sql.
-- Orden: hijos antes que padres para respetar FKs.
--
-- IDs eliminados:
--   Lider:       9001 – 9003
--   Cartera:     9001 – 9003
--   AgenteID:    90000001 – 90000020
--   TitularID:   70000001 – 70000060
--   AspNetUsers: 90001 – 90023
-- ============================================================

-- ============================================================
--  BLOQUE 1 — DBZentinela (primero: romper FK de UserRoles)
-- ============================================================
USE DBZentinela;
GO

-- 1.1 Roles de usuario
DELETE FROM dbo.AspNetUserRoles
WHERE UserId BETWEEN 90001 AND 90023;

-- 1.2 Usuarios (asesores y líderes)
SET IDENTITY_INSERT dbo.AspNetUsers ON;
DELETE FROM dbo.AspNetUsers
WHERE Id BETWEEN 90001 AND 90023;
SET IDENTITY_INSERT dbo.AspNetUsers OFF;

-- 1.3 Roles creados por el seed (solo si no los usa ningún otro usuario)
DELETE FROM dbo.AspNetRoles
WHERE NormalizedName IN ('ASESOR','LIDER','DIRECTOR','ADMIN')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AspNetUserRoles ur
      JOIN dbo.AspNetRoles r2 ON r2.Id = ur.RoleId
      WHERE r2.NormalizedName = dbo.AspNetRoles.NormalizedName
  );

GO

-- ============================================================
--  BLOQUE 2 — DBRecursosHumanos
-- ============================================================
USE DBRecursosHumanos;
GO

-- 2.1 Empleados de ejemplo
SET IDENTITY_INSERT dbo.Empleado ON;
DELETE FROM dbo.Empleado
WHERE NumeroDocumento BETWEEN 90000001 AND 90000020;
SET IDENTITY_INSERT dbo.Empleado OFF;

-- 2.2 Cargo de ejemplo (solo si ningún otro empleado lo referencia)
IF NOT EXISTS (
    SELECT 1 FROM dbo.Empleado WHERE CargoID = 9000
      AND NumeroDocumento NOT BETWEEN 90000001 AND 90000020
)
BEGIN
    SET IDENTITY_INSERT dbo.Cargo ON;
    DELETE FROM dbo.Cargo WHERE ID = 9000;
    SET IDENTITY_INSERT dbo.Cargo OFF;
END;

GO

-- ============================================================
--  BLOQUE 3 — DBCallCenter (hijos primero)
-- ============================================================
USE DBCallCenter;
GO

-- 3.1 Pagos (seed_ejemplos_pagos.sql)
DELETE FROM dbo.Pago
WHERE AgenteControlID BETWEEN 90000001 AND 90000020;

-- 3.2 Metas por agente
DELETE FROM dbo.Meta_Agente_Mes
WHERE AgenteID BETWEEN 90000001 AND 90000020
  AND Anio = 2026 AND Mes = 4;

-- 3.3 Metas por líder-cartera
DELETE FROM dbo.Meta_Lider_Cartera_Mes
WHERE LiderID BETWEEN 9001 AND 9003
  AND Anio = 2026 AND Mes = 4;

-- 3.4 UltimoEstadoTitular
DELETE FROM dbo.UltimoEstadoTitular
WHERE AgenteID BETWEEN 90000001 AND 90000020;

-- 3.6 Cartera_Agente
DELETE FROM dbo.Cartera_Agente
WHERE AgenteID BETWEEN 90000001 AND 90000020;

-- 3.7 Agentes
DELETE FROM dbo.Agente
WHERE AgenteID BETWEEN 90000001 AND 90000020;

-- 3.8 Titular_Cartera
DELETE FROM dbo.Titular_Cartera
WHERE TitularID BETWEEN 70000001 AND 70000060;

-- 3.9 Titulares
DELETE FROM dbo.Titular
WHERE NumeroDocumento BETWEEN 70000001 AND 70000060;

-- 3.10 Lider_Cartera
DELETE FROM dbo.Lider_Cartera
WHERE LiderID BETWEEN 9001 AND 9003;

-- 3.11 Carteras
SET IDENTITY_INSERT dbo.Cartera ON;
DELETE FROM dbo.Cartera WHERE ID BETWEEN 9001 AND 9003;
SET IDENTITY_INSERT dbo.Cartera OFF;

-- 3.12 Líderes
SET IDENTITY_INSERT dbo.Lider ON;
DELETE FROM dbo.Lider WHERE ID BETWEEN 9001 AND 9003;
SET IDENTITY_INSERT dbo.Lider OFF;

GO

-- ============================================================
-- VERIFICACIÓN (descomentar para confirmar limpieza)
-- ============================================================
/*
USE DBCallCenter;
SELECT COUNT(*) AS Lideres   FROM dbo.Lider         WHERE ID BETWEEN 9001 AND 9003;          -- debe ser 0
SELECT COUNT(*) AS Carteras  FROM dbo.Cartera        WHERE ID BETWEEN 9001 AND 9003;          -- debe ser 0
SELECT COUNT(*) AS Titulares FROM dbo.Titular        WHERE NumeroDocumento BETWEEN 70000001 AND 70000060; -- debe ser 0
SELECT COUNT(*) AS Agentes   FROM dbo.Agente         WHERE AgenteID BETWEEN 90000001 AND 90000020;       -- debe ser 0
SELECT COUNT(*) AS Gestiones FROM dbo.UltimoEstadoTitular WHERE AgenteID BETWEEN 90000001 AND 90000020;  -- debe ser 0

USE DBRecursosHumanos;
SELECT COUNT(*) AS Empleados FROM dbo.Empleado WHERE NumeroDocumento BETWEEN 90000001 AND 90000020; -- debe ser 0

USE DBZentinela;
SELECT COUNT(*) AS Usuarios FROM dbo.AspNetUsers WHERE Id BETWEEN 90001 AND 90023; -- debe ser 0
*/

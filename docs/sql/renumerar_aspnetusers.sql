-- ============================================================
-- renumerar_aspnetusers.sql
-- Renumera AspNetUsers.Id en orden por Id ascendente,
-- asignando IDs consecutivos 1, 2, 3, ...
-- AdminZentinela queda en Id=1 (era 1, no cambia).
--
-- Pre-condiciones:
--   • Solo AspNetUserRoles tiene filas (9). Claims/Logins/Tokens vacíos.
--   • DBCallCenter / DBRecursosHumanos no referencian AspNetUsers.Id
--     (las relaciones cruzadas se hacen vía NumeroDocumento, no FK).
-- ============================================================

USE DBZentinela;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Capturar usuarios con su nuevo ID
DECLARE @users TABLE (
    NewID                  INT,
    OldID                  INT,
    LiderID                INT,
    NombreCompleto         NVARCHAR(MAX),
    UserName               NVARCHAR(256),
    NormalizedUserName     NVARCHAR(256),
    Email                  NVARCHAR(256),
    NormalizedEmail        NVARCHAR(256),
    EmailConfirmed         BIT,
    PasswordHash           NVARCHAR(MAX),
    SecurityStamp          NVARCHAR(MAX),
    ConcurrencyStamp       NVARCHAR(MAX),
    PhoneNumber            NVARCHAR(MAX),
    PhoneNumberConfirmed   BIT,
    TwoFactorEnabled       BIT,
    LockoutEnd             DATETIMEOFFSET,
    LockoutEnabled         BIT,
    AccessFailedCount      INT,
    NumeroDocumento        BIGINT,
    RequiereCambioPassword BIT
);

INSERT INTO @users
SELECT
    ROW_NUMBER() OVER (ORDER BY Id ASC) AS NewID,
    Id, LiderID, NombreCompleto, UserName, NormalizedUserName,
    Email, NormalizedEmail, EmailConfirmed,
    PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
    LockoutEnd, LockoutEnabled, AccessFailedCount,
    NumeroDocumento, RequiereCambioPassword
FROM dbo.AspNetUsers;

-- 2. Capturar AspNetUserRoles con UserId mapeado a NewID
DECLARE @roles TABLE (NewUserID INT, RoleId INT);
INSERT INTO @roles (NewUserID, RoleId)
SELECT u.NewID, ur.RoleId
FROM dbo.AspNetUserRoles ur
JOIN @users u ON u.OldID = ur.UserId;

DECLARE @maxNew INT = (SELECT MAX(NewID) FROM @users);
DECLARE @cuser  INT = (SELECT COUNT(*)   FROM @users);
DECLARE @crole  INT = (SELECT COUNT(*)   FROM @roles);
PRINT CONCAT('Usuarios a renumerar: ', @cuser, '   Roles a re-mapear: ', @crole, '   Nuevo MAX(Id): ', @maxNew);

-- 3. Borrar las hijas (Claims/Logins/Tokens están vacías; defensivo igual)
DELETE FROM dbo.AspNetUserRoles;
DELETE FROM dbo.AspNetUserClaims;
DELETE FROM dbo.AspNetUserLogins;
DELETE FROM dbo.AspNetUserTokens;

-- 4. Borrar AspNetUsers (después de remover FKs)
DELETE FROM dbo.AspNetUsers;

-- 5. Re-insertar AspNetUsers con IDs consecutivos
SET IDENTITY_INSERT dbo.AspNetUsers ON;

INSERT INTO dbo.AspNetUsers (
    Id, LiderID, NombreCompleto, UserName, NormalizedUserName,
    Email, NormalizedEmail, EmailConfirmed,
    PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
    LockoutEnd, LockoutEnabled, AccessFailedCount,
    NumeroDocumento, RequiereCambioPassword
)
SELECT
    NewID, LiderID, NombreCompleto, UserName, NormalizedUserName,
    Email, NormalizedEmail, EmailConfirmed,
    PasswordHash, SecurityStamp, ConcurrencyStamp,
    PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
    LockoutEnd, LockoutEnabled, AccessFailedCount,
    NumeroDocumento, RequiereCambioPassword
FROM @users
ORDER BY NewID;

SET IDENTITY_INSERT dbo.AspNetUsers OFF;

-- 6. Re-poblar AspNetUserRoles con los nuevos UserIDs
INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT NewUserID, RoleId FROM @roles;

COMMIT TRANSACTION;
GO

-- 7. Reseed para que el próximo INSERT continúe la secuencia
DECLARE @max INT = (SELECT ISNULL(MAX(Id), 0) FROM dbo.AspNetUsers);
DBCC CHECKIDENT('dbo.AspNetUsers', RESEED, @max);
GO

-- Verificación
SELECT 'AspNetUsers tras renumerado' AS Diag;
SELECT u.Id, u.UserName, r.Name AS Rol
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
ORDER BY u.Id;
GO

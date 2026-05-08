-- ============================================================
-- delete_asesor_ejemplo_y_renumerar.sql
-- Borra el usuario asesor@zentinela.com y renumera AspNetUsers
-- consecutivamente desde 1.
-- ============================================================

USE DBZentinela;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Borrar al asesor de ejemplo (y sus roles/claims/logins/tokens)
DECLARE @asesorID INT = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'ASESOR@ZENTINELA.COM');

IF @asesorID IS NULL
BEGIN
    RAISERROR('No se encontró asesor@zentinela.com', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

DELETE FROM dbo.AspNetUserRoles  WHERE UserId = @asesorID;
DELETE FROM dbo.AspNetUserClaims WHERE UserId = @asesorID;
DELETE FROM dbo.AspNetUserLogins WHERE UserId = @asesorID;
DELETE FROM dbo.AspNetUserTokens WHERE UserId = @asesorID;
DELETE FROM dbo.AspNetUsers      WHERE Id     = @asesorID;

PRINT CONCAT('Usuario asesor@zentinela.com (Id=', @asesorID, ') eliminado.');

-- 2. Capturar usuarios restantes con NewID
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

-- 3. Capturar roles con NewUserID
DECLARE @roles TABLE (NewUserID INT, RoleId INT);
INSERT INTO @roles (NewUserID, RoleId)
SELECT u.NewID, ur.RoleId
FROM dbo.AspNetUserRoles ur
JOIN @users u ON u.OldID = ur.UserId;

DECLARE @maxNew INT = (SELECT MAX(NewID) FROM @users);
DECLARE @cuser  INT = (SELECT COUNT(*)   FROM @users);
DECLARE @crole  INT = (SELECT COUNT(*)   FROM @roles);
PRINT CONCAT('Renumerar usuarios: ', @cuser, '   Roles: ', @crole, '   Nuevo MAX(Id): ', @maxNew);

-- 4. Limpiar tablas hijas
DELETE FROM dbo.AspNetUserRoles;
DELETE FROM dbo.AspNetUserClaims;
DELETE FROM dbo.AspNetUserLogins;
DELETE FROM dbo.AspNetUserTokens;

-- 5. Borrar AspNetUsers
DELETE FROM dbo.AspNetUsers;

-- 6. Re-insertar con IDs consecutivos
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

-- 7. Re-poblar roles
INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT NewUserID, RoleId FROM @roles;

COMMIT TRANSACTION;
GO

-- 8. Reseed IDENTITY al MAX(Id)
DECLARE @max INT = (SELECT ISNULL(MAX(Id), 0) FROM dbo.AspNetUsers);
DBCC CHECKIDENT('dbo.AspNetUsers', RESEED, @max);
GO

-- Verificación
SELECT 'AspNetUsers final' AS Diag;
SELECT u.Id, u.UserName, r.Name AS Rol
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
ORDER BY u.Id;
GO

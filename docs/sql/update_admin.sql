-- ============================================================
-- update_admin.sql
-- Actualiza el usuario administrador (Id = 1) con nuevas
-- credenciales: UserName "AdminZentinela" + password definida.
-- Mantiene Id, rol Admin y demás FKs intactas.
--
-- Hash generado con tools/HashGen/Program.cs (PasswordHasher<T> V3).
-- Password en claro: 4dm1n1str4d0rZ3nt1n3l4
-- ============================================================

USE DBZentinela;
GO

DECLARE @Hash NVARCHAR(MAX) = N'AQAAAAEAACcQAAAAEAR0Q6TbhIdogZyTRlNIETicTCGq4cKt4qDCoHEVg4Oz50oC3tCebUNZtp6qMZzo1Q==';

UPDATE dbo.AspNetUsers
SET
    UserName            = N'AdminZentinela',
    NormalizedUserName  = N'ADMINZENTINELA',
    Email               = N'AdminZentinela@zentinela.local',
    NormalizedEmail     = N'ADMINZENTINELA@ZENTINELA.LOCAL',
    EmailConfirmed      = 1,
    PasswordHash        = @Hash,
    SecurityStamp       = CONVERT(NVARCHAR(MAX), NEWID()),
    ConcurrencyStamp    = CONVERT(NVARCHAR(MAX), NEWID()),
    LockoutEnd          = NULL,
    AccessFailedCount   = 0,
    RequiereCambioPassword = 0
WHERE Id = 1;

-- Verificación
SELECT 'Admin tras actualización' AS Diag;
SELECT u.Id, u.UserName, u.Email, r.Name AS Rol
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE u.Id = 1;
GO

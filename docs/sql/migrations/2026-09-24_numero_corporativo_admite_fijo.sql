-- ============================================================================
-- El número corporativo del asesor admite fijo nacional — DBCallCenter
--
--   CK_Agente_NumeroCorporativo solo aceptaba móviles (3 + 9 dígitos). Hay asesores
--   que atienden desde una línea fija corporativa (6 + 9 dígitos desde la migración
--   de 2021 a diez dígitos), y con la restricción anterior guardar ese número
--   fallaba en la base aunque la UI lo dejara escribir.
--
--   Se amplía a [36]. NO se abre más que eso: el número tiene que seguir siendo de
--   diez dígitos y empezar por un indicativo válido, porque de ahí sale el
--   {CELULAR_ASESOR} que el titular ve en el SMS.
--
--   Idempotente: recrea la restricción solo si sigue siendo la vieja.
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>
--   Fecha: 2026-09-24.
-- ============================================================================

USE DBCallCenter;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Agente_NumeroCorporativo'
             AND definition LIKE '%[[]3[]]%')
BEGIN
    ALTER TABLE dbo.Agente DROP CONSTRAINT CK_Agente_NumeroCorporativo;
    PRINT 'Restriccion vieja (solo moviles) eliminada.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Agente_NumeroCorporativo')
BEGIN
    ALTER TABLE dbo.Agente WITH CHECK ADD CONSTRAINT CK_Agente_NumeroCorporativo
        CHECK (NumeroCorporativo IS NULL
               OR (LEN(NumeroCorporativo) = 10
                   AND NumeroCorporativo LIKE '[36][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'));
    PRINT 'Restriccion recreada: admite movil (3...) y fijo nacional (6...).';
END
GO

-- Verificación
SELECT name AS restriccion, definition
FROM sys.check_constraints
WHERE name = 'CK_Agente_NumeroCorporativo';
GO

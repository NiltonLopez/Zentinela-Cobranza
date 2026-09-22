-- ============================================================================
-- Migración: 2026-08-14 — Eliminar HistoricoVirtualidad.FechaResultado
-- ============================================================================
-- Motivo: el canal IVR pasa de dos fases (Solicitud + Resultado) a una carga
-- única del archivo del marcador, que ya trae el estado de cada llamada. La
-- columna era WRITE-ONLY: una sola escritura en todo el repositorio
-- (HistoricoVirtualidadService.ConfirmarResultadoAsync) y CERO lecturas —
-- ningún informe, export, DTO ni consulta la usaba. Ver ADR-031.
--
-- ┌────────────────────────────────────────────────────────────────────────┐
-- │ ORDEN DE APLICACIÓN — el CÓDIGO PRIMERO, este script DESPUÉS.          │
-- └────────────────────────────────────────────────────────────────────────┘
--   1. Desplegar la versión de Zentinela que ya NO mapea FechaResultado
--      (Core/Entities/HistoricoVirtualidad.cs y
--       Infrastructure/.../Mappings/HistoricoVirtualidadMapping.cs).
--   2. Verificar que responden los cuatro consumidores de la tabla:
--        - Informes → Virtualidad (tab IVR)
--        - Informes → Virtualidad (tab SMS / Mensajería)
--        - el gráfico Pagos vs Canal de Impacto
--        - Administración → Gestión de Carteras (guard de borrado de un par
--          líder-cartera con histórico)
--   3. Recién entonces, ejecutar este script.
--
-- El orden inverso ROMPE la aplicación en producción: EF Core seguiría
-- generando "SELECT ... FechaResultado ..." en TODA lectura de la tabla y los
-- cuatro consumidores fallarían. En el orden correcto el intervalo intermedio
-- es inofensivo: una columna nullable que nadie mapea, lee ni escribe.
--
-- Sin pérdida de datos: al momento del análisis la columna estaba en NULL en
-- las 239.701 filas existentes. El script aborta si encuentra algún valor.
-- ============================================================================

USE DBCallCenter;
GO

-- Guarda: si alguien pobló la columna después del análisis, no seguimos.
IF EXISTS (SELECT 1 FROM dbo.HistoricoVirtualidad WHERE FechaResultado IS NOT NULL)
BEGIN
    RAISERROR('ABORTADO: HistoricoVirtualidad.FechaResultado tiene valores no nulos. Revisar antes de eliminar.', 16, 1);
    RETURN;
END
GO

IF EXISTS (
    SELECT 1
      FROM sys.columns
     WHERE object_id = OBJECT_ID('dbo.HistoricoVirtualidad')
       AND name      = 'FechaResultado'
)
BEGIN
    -- En producción la columna no tiene DEFAULT propio, pero si algún entorno lo
    -- tuviera, el ALTER fallaría sin soltarlo antes.
    DECLARE @df sysname, @sql nvarchar(max);

    SELECT @df = dc.name
      FROM sys.default_constraints dc
      JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
     WHERE dc.parent_object_id = OBJECT_ID('dbo.HistoricoVirtualidad')
       AND c.name = 'FechaResultado';

    -- EXEC() no acepta una expresión concatenada: hay que armar la cadena en una
    -- variable. Con EXEC('...' + QUOTENAME(@df)) el batch ni siquiera compila,
    -- así que el DROP de abajo tampoco se ejecutaba (corregido 2026-09-22).
    IF @df IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.HistoricoVirtualidad DROP CONSTRAINT ' + QUOTENAME(@df);
        EXEC sp_executesql @sql;
    END

    ALTER TABLE dbo.HistoricoVirtualidad DROP COLUMN FechaResultado;

    PRINT 'OK: HistoricoVirtualidad.FechaResultado eliminada.';
END
ELSE
    PRINT 'La columna FechaResultado ya no existe — nada que hacer.';
GO

-- ============================================================================
-- Rollback (la columna vuelve vacía; no había dato original que restaurar):
--   ALTER TABLE dbo.HistoricoVirtualidad ADD FechaResultado DATETIME2 NULL;
-- ============================================================================

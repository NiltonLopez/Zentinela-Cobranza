-- ============================================================================
-- Migración: 2026-07-08 — Renombrar SolicitudVirtualidad → HistoricoVirtualidad
-- ============================================================================
-- Motivo: alinear el nombre de la tabla con el lineamiento de nombres de la
-- BD (pedido del usuario). La tabla estaba vacía en producción (0 filas) al
-- momento del rename — sin riesgo de datos. No hay FKs de otras tablas hacia
-- esta (verificado con sys.foreign_keys antes de aplicar).
--
-- Renombra tabla + constraints + índices. Ver docs/sql/create_SolicitudVirtualidad.sql
-- para la definición original (se deja intacto como registro histórico).
--
-- Reversible: ver bloque ROLLBACK al final.
-- ============================================================================

USE DBCallCenter;
GO

IF OBJECT_ID('dbo.SolicitudVirtualidad', 'U') IS NOT NULL
BEGIN
    EXEC sp_rename 'dbo.SolicitudVirtualidad', 'HistoricoVirtualidad';
    EXEC sp_rename 'dbo.PK_SolicitudVirtualidad', 'PK_HistoricoVirtualidad';
    EXEC sp_rename 'dbo.DF_SolicitudVirtualidad_FechaCargue', 'DF_HistoricoVirtualidad_FechaCargue';
    EXEC sp_rename 'dbo.CK_SolicitudVirtualidad_Canal', 'CK_HistoricoVirtualidad_Canal';
    EXEC sp_rename 'dbo.FK_SolicitudVirtualidad_LiderCartera', 'FK_HistoricoVirtualidad_LiderCartera';
    EXEC sp_rename 'dbo.HistoricoVirtualidad.IX_SolicitudVirtualidad_Canal_Cartera_Fecha', 'IX_HistoricoVirtualidad_Canal_Cartera_Fecha', 'INDEX';
    EXEC sp_rename 'dbo.HistoricoVirtualidad.IX_SolicitudVirtualidad_Canal_Destino', 'IX_HistoricoVirtualidad_Canal_Destino', 'INDEX';
END
GO

-- Rollback:
-- EXEC sp_rename 'dbo.HistoricoVirtualidad', 'SolicitudVirtualidad';
-- EXEC sp_rename 'dbo.PK_HistoricoVirtualidad', 'PK_SolicitudVirtualidad';
-- EXEC sp_rename 'dbo.DF_HistoricoVirtualidad_FechaCargue', 'DF_SolicitudVirtualidad_FechaCargue';
-- EXEC sp_rename 'dbo.CK_HistoricoVirtualidad_Canal', 'CK_SolicitudVirtualidad_Canal';
-- EXEC sp_rename 'dbo.FK_HistoricoVirtualidad_LiderCartera', 'FK_SolicitudVirtualidad_LiderCartera';
-- EXEC sp_rename 'dbo.SolicitudVirtualidad.IX_HistoricoVirtualidad_Canal_Cartera_Fecha', 'IX_SolicitudVirtualidad_Canal_Cartera_Fecha', 'INDEX';
-- EXEC sp_rename 'dbo.SolicitudVirtualidad.IX_HistoricoVirtualidad_Canal_Destino', 'IX_SolicitudVirtualidad_Canal_Destino', 'INDEX';

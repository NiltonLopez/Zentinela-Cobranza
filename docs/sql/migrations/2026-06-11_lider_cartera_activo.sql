-- =====================================================================
-- Migración: 2026-06-11 — Lider_Cartera.Activo (soft-delete / retiro)
-- =====================================================================
-- ESTADO: APLICADA en DBCallCenter el 2026-06-11 (54 relaciones, todas
--         Activo=1). Backup: dbo.Lider_Cartera_BAK_20260611 (54 filas).
-- =====================================================================
-- Motivo: permitir RETIRAR una relación líder-cartera sin borrarla, para
--   preservar el histórico de pagos, metas, mensajería e IVR que quedan
--   anclados al par (LiderID, CarteraID). Ver ADR-022.
--
--   Una relación con Activo = 0:
--     - Desaparece de lo OPERATIVO: generación de bases, selectores de
--       Cargue de Pagos, Calificador, Comparador, Asignación de Metas,
--       Dashboard Director, carteras del agente.
--     - SIGUE VISIBLE en lo HISTÓRICO: Informes de Recaudo (NO filtra por
--       Activo) — ahí debe seguir apareciendo el recaudo del líder retirado.
--     - Se muestra en Gestión de Líderes y Carteras con badge "Inactiva"
--       y puede REACTIVARSE (Activo = 1).
--
--   El flag vive en la relación (no en la cartera), igual que EsExterna:
--   una cartera puede tener una relación activa con un líder y otra
--   inactiva con otro. Activo y EsExterna son ortogonales.
--
-- Cambios:
--   1) ALTER TABLE dbo.Lider_Cartera ADD Activo BIT NOT NULL DEFAULT 1.
--      (Todas las relaciones existentes quedan activas — sin cambio de
--       comportamiento operativo al aplicar.)
--
-- Backup previo recomendado:
--   SELECT * INTO dbo.Lider_Cartera_BAK_20260611 FROM dbo.Lider_Cartera;
--
-- Idempotente: comprueba sys.columns antes de agregar.
-- Reversible:
--   ALTER TABLE dbo.Lider_Cartera DROP CONSTRAINT DF_Lider_Cartera_Activo;
--   ALTER TABLE dbo.Lider_Cartera DROP COLUMN Activo;
--
-- BD: DBCallCenter (10.0.0.89,1433).
-- =====================================================================

USE DBCallCenter;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Lider_Cartera')
      AND name = N'Activo'
)
BEGIN
    ALTER TABLE dbo.Lider_Cartera
        ADD Activo BIT NOT NULL CONSTRAINT DF_Lider_Cartera_Activo DEFAULT 1;
END
GO

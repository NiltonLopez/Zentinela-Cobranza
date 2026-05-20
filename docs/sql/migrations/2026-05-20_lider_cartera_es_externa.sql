-- =====================================================================
-- Migración: 2026-05-20 — Lider_Cartera.EsExterna (relación externa)
-- =====================================================================
-- Motivo: distinguir relaciones líder-cartera operadas por agencias externas
--   o personal externo, manteniendo la posibilidad de que la MISMA cartera
--   sea trabajada simultáneamente por:
--     - un líder externo (relación con EsExterna = 1), y
--     - un líder interno (relación con EsExterna = 0).
--
--   Las relaciones con EsExterna = 1 quedan excluidas de:
--     - Informes administrativos de recaudo
--     - Metas (Meta_Lider_Cartera_Mes)
--     - Dashboard Director y Comparador de Carteras
--     - Selector de Cargue de Pagos
--
--   Y se incluyen en el sub-módulo "Informes → Agencias Externas".
--
--   La marca vive en la relación, no en la cartera, porque una cartera
--   puede tener simultáneamente relaciones internas y externas. Decisión
--   de modelado (2026-05-20).
--
-- Cambios:
--   1) ALTER TABLE dbo.Lider_Cartera ADD EsExterna BIT NOT NULL DEFAULT 0.
--
-- Backup previo:
--   Lider_Cartera_BAK_20260520 (53 filas al 2026-05-20).
--
-- Idempotente: comprueba sys.columns antes de agregar.
-- Reversible:
--   ALTER TABLE dbo.Lider_Cartera DROP CONSTRAINT DF_Lider_Cartera_EsExterna;
--   ALTER TABLE dbo.Lider_Cartera DROP COLUMN EsExterna;
--
-- BD: DBCallCenter (10.0.0.89,1433).
-- =====================================================================

USE DBCallCenter;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Lider_Cartera')
      AND name = N'EsExterna'
)
BEGIN
    ALTER TABLE dbo.Lider_Cartera
        ADD EsExterna BIT NOT NULL CONSTRAINT DF_Lider_Cartera_EsExterna DEFAULT 0;
END
GO

-- ─────────────────────────────────────────────────────────────────────
-- Seed inicial: marcar las relaciones externas conocidas.
-- TODO: validar/ajustar con la lista real de líderes externos antes de ejecutar.
-- ─────────────────────────────────────────────────────────────────────
-- Ejemplos:
-- UPDATE lc SET EsExterna = 1
-- FROM dbo.Lider_Cartera lc
-- INNER JOIN dbo.Lider l ON l.ID = lc.LiderID
-- WHERE l.Nombre IN ('Hugo Betancur', 'Adriana Molina');
--
-- UPDATE lc SET EsExterna = 1
-- FROM dbo.Lider_Cartera lc
-- INNER JOIN dbo.Lider l   ON l.ID   = lc.LiderID
-- INNER JOIN dbo.Cartera c ON c.ID   = lc.CarteraID
-- WHERE l.Nombre = 'Manuela …' AND c.NombreCartera = 'Mercadeos Personal';

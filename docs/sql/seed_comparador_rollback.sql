-- ============================================================
-- seed_comparador_rollback.sql — Limpieza exhaustiva de seeds
-- ============================================================
-- Borra de raíz los seeds del Comparador y termina lo que el
-- rollback original no pudo (Lider_Cartera/Cartera/Lider) por
-- huérfanos en otras tablas hijas.
--
-- Estrategia: limpiar TODAS las tablas hijas que apuntan a
-- Titular, Lider_Cartera, Cartera y Lider en los rangos seed.
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE DBCallCenter;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @TitMin BIGINT = 70000061;
DECLARE @TitMax BIGINT = 70000080;

-- ── 1. Tablas hijas de Titular (rango Comparador) ───────────────
DELETE FROM dbo.UltimoEstadoTitular WHERE TitularID BETWEEN @TitMin AND @TitMax;

DELETE FROM dbo.ARLSura          WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Cajacopi         WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Camara           WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Codeudor         WHERE TitularRelacionado BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Compensados      WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Fosyga           WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Positiva         WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.PositivaEmpresas WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.RGM              WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.RUES             WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.SaludTotal       WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.SIMIT            WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.SOS              WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Tigo             WHERE TitularID BETWEEN @TitMin AND @TitMax;

-- ── 2. Pago para titulares seed (defensivo) ─────────────────────
DELETE FROM dbo.Pago WHERE CedulaTitular BETWEEN @TitMin AND @TitMax;

-- ── 3. Titular_Cartera del Comparador y residuales del seed Lider ──
DELETE FROM dbo.Titular_Cartera WHERE TitularID BETWEEN @TitMin AND @TitMax;
DELETE FROM dbo.Titular_Cartera WHERE LiderID BETWEEN 9001 AND 9003;

-- ── 4. Titulares del Comparador ─────────────────────────────────
DELETE FROM dbo.Titular WHERE NumeroDocumento BETWEEN @TitMin AND @TitMax;

-- ── 5. Tablas hijas de Lider_Cartera para IDs seed ──────────────
DELETE FROM dbo.Cartera_Agente
WHERE (LiderID BETWEEN 9001 AND 9003) OR (CarteraID BETWEEN 9001 AND 9003);

DELETE FROM dbo.IVREnviados
WHERE (LiderID BETWEEN 9001 AND 9003) OR (CarteraID BETWEEN 9001 AND 9003);

DELETE FROM dbo.MensajesEnviados
WHERE (LiderID BETWEEN 9001 AND 9003) OR (CarteraID BETWEEN 9001 AND 9003);

DELETE FROM dbo.Meta_Lider_Cartera_Mes
WHERE LiderID BETWEEN 9001 AND 9003 OR CarteraID BETWEEN 9001 AND 9003;

-- ── 6. Pago directos por Cartera/Lider seed (defensivo) ─────────
DELETE FROM dbo.Pago WHERE CarteraID BETWEEN 9001 AND 9003 OR LiderID BETWEEN 9001 AND 9003;

-- ── 7. Lider_Cartera, Cartera, Lider seed ───────────────────────
DELETE FROM dbo.Lider_Cartera WHERE LiderID BETWEEN 9001 AND 9003;
DELETE FROM dbo.Cartera        WHERE ID     BETWEEN 9001 AND 9003;
DELETE FROM dbo.Lider          WHERE ID     BETWEEN 9001 AND 9003;

COMMIT TRANSACTION;
GO

-- ── Verificación final ──────────────────────────────────────────
SELECT 'Lider seed restantes'             AS K, COUNT(*) AS V FROM dbo.Lider          WHERE ID BETWEEN 9001 AND 9003
UNION ALL SELECT 'Cartera seed restantes',          COUNT(*) FROM dbo.Cartera        WHERE ID     BETWEEN 9001 AND 9003
UNION ALL SELECT 'Lider_Cartera seed restantes',    COUNT(*) FROM dbo.Lider_Cartera  WHERE LiderID BETWEEN 9001 AND 9003
UNION ALL SELECT 'Titular Comparador restantes',    COUNT(*) FROM dbo.Titular        WHERE NumeroDocumento BETWEEN 70000061 AND 70000080
UNION ALL SELECT 'Titular_Cartera Comparador rest.',COUNT(*) FROM dbo.Titular_Cartera WHERE TitularID BETWEEN 70000061 AND 70000080
UNION ALL SELECT 'UltimoEstado Comparador rest.',   COUNT(*) FROM dbo.UltimoEstadoTitular WHERE TitularID BETWEEN 70000061 AND 70000080
UNION ALL SELECT 'Compensados Comparador rest.',    COUNT(*) FROM dbo.Compensados    WHERE TitularID BETWEEN 70000061 AND 70000080;
GO

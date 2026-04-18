-- ============================================================
-- seed_ejemplos_pagos.sql — Pagos y metas de prueba (Abril 2026)
-- ============================================================
-- Complemento de seed_ejemplos.sql.
-- Requiere que seed_ejemplos.sql ya haya sido ejecutado.
--
-- Agrega en DBCallCenter:
--   · Pago          — 2-3 pagos por asesor distribuidos en abril 2026
--   · Meta_Agente_Mes       — meta mensual por asesor (abril 2026)
--   · Meta_Lider_Cartera_Mes — meta mensual por líder-cartera (abril 2026)
--
-- UsuarioControlID: ID del líder en DBZentinela.AspNetUsers
--   Cartera 9001 → UsuarioControlID = 90021 (lider.ejemplo1)
--   Cartera 9002 → UsuarioControlID = 90022 (lider.ejemplo2)
--   Cartera 9003 → UsuarioControlID = 90023 (lider.ejemplo3)
-- ============================================================
USE DBCallCenter;
GO

-- ============================================================
--  BLOQUE 1 — Pagos (Pago usa IDENTITY — no necesita SET)
-- ============================================================
-- 2-3 pagos por asesor en días distintos de abril 2026.
-- ValorPagado alineado con las promesas de UltimoEstadoTitular.
-- Honorarios = 10 % del ValorPagado.

INSERT INTO dbo.Pago
    (CedulaTitular, NombreCliente, ValorPagado, FechaPago,
     Honorarios, LiderID, CarteraID, UsuarioControlID, AgenteControlID)
SELECT v.ced, v.nom, v.valor, v.fecha, ROUND(v.valor * 0.10, 0), v.lid, v.car, v.usr, v.age
FROM (VALUES
    -- ── Asesor 1 (90000001) · Cartera 9001 ──
    (70000001,'Deudor Ejemplo 01', 900000.00, '2026-04-05', 9001,9001,90021,90000001),
    (70000001,'Deudor Ejemplo 01', 900000.00, '2026-04-15', 9001,9001,90021,90000001),
    -- ── Asesor 2 (90000002) · Cartera 9001 ──
    (70000004,'Deudor Ejemplo 04',1250000.00, '2026-04-07', 9001,9001,90021,90000002),
    (70000004,'Deudor Ejemplo 04',1250000.00, '2026-04-14', 9001,9001,90021,90000002),
    -- ── Asesor 3 (90000003) · Cartera 9001 ──
    (70000007,'Deudor Ejemplo 07', 475000.00, '2026-04-08', 9001,9001,90021,90000003),
    (70000007,'Deudor Ejemplo 07', 475000.00, '2026-04-16', 9001,9001,90021,90000003),
    -- ── Asesor 4 (90000004) · Cartera 9001 ──
    (70000010,'Deudor Ejemplo 10',1600000.00, '2026-04-05', 9001,9001,90021,90000004),
    (70000010,'Deudor Ejemplo 10',1600000.00, '2026-04-12', 9001,9001,90021,90000004),
    -- ── Asesor 5 (90000005) · Cartera 9001 ──
    (70000013,'Deudor Ejemplo 13', 750000.00, '2026-04-10', 9001,9001,90021,90000005),
    (70000013,'Deudor Ejemplo 13', 750000.00, '2026-04-17', 9001,9001,90021,90000005),
    -- ── Asesor 6 (90000006) · Cartera 9001 ──
    (70000016,'Deudor Ejemplo 16',2050000.00, '2026-04-06', 9001,9001,90021,90000006),
    (70000016,'Deudor Ejemplo 16',2050000.00, '2026-04-13', 9001,9001,90021,90000006),
    -- ── Asesor 7 (90000007) · Cartera 9001 ──
    (70000019,'Deudor Ejemplo 19', 390000.00, '2026-04-09', 9001,9001,90021,90000007),
    (70000019,'Deudor Ejemplo 19', 390000.00, '2026-04-16', 9001,9001,90021,90000007),
    -- ── Asesor 8 (90000008) · Cartera 9002 ──
    (70000022,'Deudor Ejemplo 22', 933000.00, '2026-04-05', 9002,9002,90022,90000008),
    (70000022,'Deudor Ejemplo 22', 933000.00, '2026-04-11', 9002,9002,90022,90000008),
    (70000022,'Deudor Ejemplo 22', 934000.00, '2026-04-17', 9002,9002,90022,90000008),
    -- ── Asesor 9 (90000009) · Cartera 9002 ──
    (70000025,'Deudor Ejemplo 25', 625000.00, '2026-04-08', 9002,9002,90022,90000009),
    (70000025,'Deudor Ejemplo 25', 625000.00, '2026-04-15', 9002,9002,90022,90000009),
    -- ── Asesor 10 (90000010) · Cartera 9002 ──
    (70000028,'Deudor Ejemplo 28',1200000.00, '2026-04-06', 9002,9002,90022,90000010),
    (70000028,'Deudor Ejemplo 28',1200000.00, '2026-04-12', 9002,9002,90022,90000010),
    (70000028,'Deudor Ejemplo 28',1200000.00, '2026-04-17', 9002,9002,90022,90000010),
    -- ── Asesor 11 (90000011) · Cartera 9002 ──
    (70000031,'Deudor Ejemplo 31', 335000.00, '2026-04-09', 9002,9002,90022,90000011),
    (70000031,'Deudor Ejemplo 31', 335000.00, '2026-04-16', 9002,9002,90022,90000011),
    -- ── Asesor 12 (90000012) · Cartera 9002 ──
    (70000034,'Deudor Ejemplo 34',1633000.00, '2026-04-07', 9002,9002,90022,90000012),
    (70000034,'Deudor Ejemplo 34',1633000.00, '2026-04-14', 9002,9002,90022,90000012),
    (70000034,'Deudor Ejemplo 34',1634000.00, '2026-04-17', 9002,9002,90022,90000012),
    -- ── Asesor 13 (90000013) · Cartera 9002 ──
    (70000037,'Deudor Ejemplo 37', 975000.00, '2026-04-10', 9002,9002,90022,90000013),
    (70000037,'Deudor Ejemplo 37', 975000.00, '2026-04-17', 9002,9002,90022,90000013),
    -- ── Asesor 14 (90000014) · Cartera 9002 ──
    (70000040,'Deudor Ejemplo 40',1150000.00, '2026-04-05', 9002,9002,90022,90000014),
    (70000040,'Deudor Ejemplo 40',1150000.00, '2026-04-13', 9002,9002,90022,90000014),
    -- ── Asesor 15 (90000015) · Cartera 9003 ──
    (70000043,'Deudor Ejemplo 43', 410000.00, '2026-04-08', 9003,9003,90023,90000015),
    (70000043,'Deudor Ejemplo 43', 410000.00, '2026-04-15', 9003,9003,90023,90000015),
    -- ── Asesor 16 (90000016) · Cartera 9003 ──
    (70000046,'Deudor Ejemplo 46',1266000.00, '2026-04-06', 9003,9003,90023,90000016),
    (70000046,'Deudor Ejemplo 46',1267000.00, '2026-04-12', 9003,9003,90023,90000016),
    (70000046,'Deudor Ejemplo 46',1267000.00, '2026-04-17', 9003,9003,90023,90000016),
    -- ── Asesor 17 (90000017) · Cartera 9003 ──
    (70000049,'Deudor Ejemplo 49', 700000.00, '2026-04-09', 9003,9003,90023,90000017),
    (70000049,'Deudor Ejemplo 49', 700000.00, '2026-04-16', 9003,9003,90023,90000017),
    -- ── Asesor 18 (90000018) · Cartera 9003 ──
    (70000052,'Deudor Ejemplo 52', 900000.00, '2026-04-07', 9003,9003,90023,90000018),
    (70000052,'Deudor Ejemplo 52', 900000.00, '2026-04-14', 9003,9003,90023,90000018),
    (70000052,'Deudor Ejemplo 52', 900000.00, '2026-04-17', 9003,9003,90023,90000018),
    -- ── Asesor 19 (90000019) · Cartera 9003 ──
    (70000055,'Deudor Ejemplo 55', 315000.00, '2026-04-10', 9003,9003,90023,90000019),
    (70000055,'Deudor Ejemplo 55', 315000.00, '2026-04-17', 9003,9003,90023,90000019),
    -- ── Asesor 20 (90000020) · Cartera 9003 ──
    (70000058,'Deudor Ejemplo 58',1433000.00, '2026-04-05', 9003,9003,90023,90000020),
    (70000058,'Deudor Ejemplo 58',1433000.00, '2026-04-11', 9003,9003,90023,90000020),
    (70000058,'Deudor Ejemplo 58',1434000.00, '2026-04-16', 9003,9003,90023,90000020)
) AS v(ced, nom, valor, fecha, lid, car, usr, age)
-- Idempotente: no insertar si ya existe pago del mismo asesor+titular+fecha
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Pago
    WHERE AgenteControlID = v.age
      AND CedulaTitular   = v.ced
      AND FechaPago       = v.fecha
);

GO

-- ============================================================
--  BLOQUE 2 — Metas por agente (Meta_Agente_Mes, Abril 2026)
-- ============================================================
-- Meta ≈ 30% sobre el recaudo real para mostrar % realista (~70-80%)
INSERT INTO dbo.Meta_Agente_Mes (AgenteID, Anio, Mes, MetaRecaudo)
SELECT v.age, 2026, 4, v.meta
FROM (VALUES
    (90000001, 2500000.00),
    (90000002, 3500000.00),
    (90000003, 1300000.00),
    (90000004, 4300000.00),
    (90000005, 2000000.00),
    (90000006, 5700000.00),
    (90000007, 1100000.00),
    (90000008, 3700000.00),
    (90000009, 1750000.00),
    (90000010, 4700000.00),
    (90000011,  950000.00),
    (90000012, 6600000.00),
    (90000013, 2700000.00),
    (90000014, 3200000.00),
    (90000015, 1100000.00),
    (90000016, 5100000.00),
    (90000017, 1900000.00),
    (90000018, 3600000.00),
    (90000019,  870000.00),
    (90000020, 5900000.00)
) AS v(age, meta)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Meta_Agente_Mes
    WHERE AgenteID = v.age AND Anio = 2026 AND Mes = 4
);

GO

-- ============================================================
--  BLOQUE 3 — Metas por líder-cartera (Meta_Lider_Cartera_Mes, Abril 2026)
-- ============================================================
-- Líder 1 recauda ≈ 19.6M → meta 25M
-- Líder 2 recauda ≈ 22.3M → meta 29M
-- Líder 3 recauda ≈ 17.5M → meta 23M
INSERT INTO dbo.Meta_Lider_Cartera_Mes (LiderID, CarteraID, Anio, Mes, MetaRecaudo)
SELECT v.lid, v.car, 2026, 4, v.meta
FROM (VALUES
    (9001, 9001, 25000000.00),
    (9002, 9002, 29000000.00),
    (9003, 9003, 23000000.00)
) AS v(lid, car, meta)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Meta_Lider_Cartera_Mes
    WHERE LiderID = v.lid AND CarteraID = v.car AND Anio = 2026 AND Mes = 4
);

GO

-- ============================================================
-- VERIFICACIÓN (descomentar para confirmar)
-- ============================================================
/*
USE DBCallCenter;
-- Total pagos insertados:
SELECT COUNT(*) AS TotalPagos,
       SUM(ValorPagado) AS TotalRecaudo,
       SUM(Honorarios)  AS TotalHonorarios
FROM dbo.Pago
WHERE AgenteControlID BETWEEN 90000001 AND 90000020;

-- Recaudo por líder:
SELECT l.Nombre AS Lider,
       SUM(p.ValorPagado) AS Recaudo,
       MAX(m.MetaRecaudo)  AS Meta,
       ROUND(SUM(p.ValorPagado) / MAX(m.MetaRecaudo) * 100, 1) AS PctCumplimiento
FROM dbo.Pago p
JOIN dbo.Meta_Lider_Cartera_Mes m ON m.LiderID=p.LiderID AND m.CarteraID=p.CarteraID AND m.Anio=2026 AND m.Mes=4
JOIN dbo.Lider l ON l.ID = p.LiderID
WHERE p.AgenteControlID BETWEEN 90000001 AND 90000020
  AND p.FechaPago BETWEEN '2026-04-01' AND '2026-04-30'
GROUP BY l.Nombre, l.ID
ORDER BY l.Nombre;

-- Metas por agente:
SELECT AgenteID, MetaRecaudo FROM dbo.Meta_Agente_Mes
WHERE AgenteID BETWEEN 90000001 AND 90000020 AND Anio=2026 AND Mes=4
ORDER BY AgenteID;
*/

-- ============================================================
-- seed_comparador.sql — Datos de prueba para Comparador de Carteras
-- ============================================================
-- Crea 20 titulares compartidos entre carteras con estados cruzados:
--   • Sin Gestión en la cartera de origen
--   • Contacto Positivo o Ya Pagó en la otra cartera
--
-- Distribución:
--   70000061–65: SinGestión en A, positivo en B (teléfonos diferentes)
--   70000066–70: SinGestión en A, positivo en C (teléfonos diferentes)
--   70000071–75: SinGestión en B, positivo en C (teléfonos diferentes)
--   70000076–80: SinGestión en C, positivo en A/B ← permite comparar desde C
--
-- Resultado al seleccionar cada cartera:
--   Cartera A → ve oportunidades en B (5) y C (5)
--   Cartera B → ve oportunidades en C (5)
--   Cartera C → ve oportunidades en A (3) y B (2)
--
-- TitularIDs: 70000061 – 70000080
-- Agentes reutilizados de seed_ejemplos.sql (90000001 – 90000019)
--
-- IMPORTANTE: Los valores de UltimoPerfil usan los nombres exactos
-- definidos en PerfilClasificacion.cs para ser clasificados correctamente:
--   YaPago:          'Pago Total' | 'Paz y Salvo' | 'Normalizado'
--   ContactoPositivo:'Contacto Positivo' | 'Promesa de Pago' | 'Acuerdo'
--   SinGestion:      todo lo demás (No Contesta, Buzón de voz, etc.)
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE DBCallCenter;
GO

-- ── 1. Titulares ──────────────────────────────────────────────
INSERT INTO dbo.Titular (NumeroDocumento, Nombre)
SELECT v.doc, v.nom
FROM (VALUES
    (70000061,'Deudor Comp 01'),(70000062,'Deudor Comp 02'),(70000063,'Deudor Comp 03'),
    (70000064,'Deudor Comp 04'),(70000065,'Deudor Comp 05'),(70000066,'Deudor Comp 06'),
    (70000067,'Deudor Comp 07'),(70000068,'Deudor Comp 08'),(70000069,'Deudor Comp 09'),
    (70000070,'Deudor Comp 10'),(70000071,'Deudor Comp 11'),(70000072,'Deudor Comp 12'),
    (70000073,'Deudor Comp 13'),(70000074,'Deudor Comp 14'),(70000075,'Deudor Comp 15')
) AS v(doc, nom)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Titular WHERE NumeroDocumento = v.doc);

-- ── 2. Titular_Cartera — cada titular aparece en 2 carteras ──
--   70000061–65: Cartera A (9001) + Cartera B (9002)
--   70000066–70: Cartera A (9001) + Cartera C (9003)
--   70000071–75: Cartera B (9002) + Cartera C (9003)
INSERT INTO dbo.Titular_Cartera (TitularID, LiderID, CarteraID)
SELECT v.t, v.l, v.c
FROM (VALUES
    (70000061,9001,9001),(70000061,9002,9002),
    (70000062,9001,9001),(70000062,9002,9002),
    (70000063,9001,9001),(70000063,9002,9002),
    (70000064,9001,9001),(70000064,9002,9002),
    (70000065,9001,9001),(70000065,9002,9002),
    (70000066,9001,9001),(70000066,9003,9003),
    (70000067,9001,9001),(70000067,9003,9003),
    (70000068,9001,9001),(70000068,9003,9003),
    (70000069,9001,9001),(70000069,9003,9003),
    (70000070,9001,9001),(70000070,9003,9003),
    (70000071,9002,9002),(70000071,9003,9003),
    (70000072,9002,9002),(70000072,9003,9003),
    (70000073,9002,9002),(70000073,9003,9003),
    (70000074,9002,9002),(70000074,9003,9003),
    (70000075,9002,9002),(70000075,9003,9003)
) AS v(t, l, c)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Titular_Cartera WHERE TitularID = v.t AND CarteraID = v.c
);

-- ── 3. UltimoEstadoTitular — estados cruzados ─────────────────
-- Columnas: TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
--           SaldoTotal, NumeroMarcado, Rotacion,
--           FechaUltimaGestion (NULL para SinGestion),
--           FechaPromesa, ValorPromesa, MotivoNoPago, FechaActualizacion

INSERT INTO dbo.UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
     SaldoTotal, NumeroMarcado, Rotacion,
     FechaUltimaGestion, FechaPromesa, ValorPromesa, MotivoNoPago,
     FechaActualizacion)
SELECT
    v.tid, v.cid, v.aid, v.up, v.mp,
    v.saldo, v.tel, v.rot,
    CONVERT(date, v.fg, 23),
    CONVERT(date, v.fp, 23),
    v.vp, v.motivo,
    CONVERT(datetime2, '2026-04-17 08:00:00', 120)
FROM (VALUES
    -- ── 70000061: Sin Gestión en Cartera A → Contacto Positivo en B ──
    (70000061,9001,90000001,'No Contesta',       NULL,                3200000,'3101234061',1, NULL,        NULL,       NULL,       'No contesta llamadas'),
    (70000061,9002,90000008,'Contacto Positivo', 'Contacto Positivo', 3200000,'3101234061',3, '2026-04-10',NULL,       NULL,       NULL),
    -- ── 70000062: Sin Gestión en Cartera A → Promesa de Pago en B ───
    (70000062,9001,90000002,N'Buz'+NCHAR(243)+N'n de voz',NULL,      5100000,'3101234062',2, NULL,        NULL,       NULL,       N'Buz'+NCHAR(243)+N'n activo'),
    (70000062,9002,90000009,'Promesa de Pago',   'Promesa de Pago',   5100000,'3101234062',4, '2026-04-12','2026-04-30',1700000.00,NULL),
    -- ── 70000063: Sin Gestión en Cartera A → Pago Total en B ────────
    (70000063,9001,90000003,'Número Errado',     NULL,                2800000,'3101234063',1, NULL,        NULL,       NULL,       'Número no corresponde'),
    (70000063,9002,90000010,'Pago Total',        'Pago Total',        2800000,'3101234063',5, '2026-04-08',NULL,       NULL,       NULL),
    -- ── 70000064: Sin Gestión en Cartera A → Acuerdo en B ───────────
    (70000064,9001,90000004,'No Contesta',       NULL,                4700000,'3101234064',2, NULL,        NULL,       NULL,       'No contesta'),
    (70000064,9002,90000011,'Acuerdo',           'Acuerdo',           4700000,'3101234064',3, '2026-04-11','2026-05-05',1566666.00,NULL),
    -- ── 70000065: Sin Gestión en Cartera A → Paz y Salvo en B ───────
    (70000065,9001,90000005,'Sin Gestión',       NULL,                6300000,'3101234065',0, NULL,        NULL,       NULL,       NULL),
    (70000065,9002,90000012,'Paz y Salvo',       'Paz y Salvo',       6300000,'3101234065',6, '2026-04-09',NULL,       NULL,       NULL),
    -- ── 70000066: Sin Gestión en Cartera A → Contacto Positivo en C ─
    (70000066,9001,90000006,'Buzón de voz',      NULL,                1900000,'3101234066',1, NULL,        NULL,       NULL,       'Buzón activo'),
    (70000066,9003,90000015,'Contacto Positivo', 'Contacto Positivo', 1900000,'3101234066',2, '2026-04-13',NULL,       NULL,       NULL),
    -- ── 70000067: Sin Gestión en Cartera A → Promesa de Pago en C ───
    (70000067,9001,90000007,'No Contesta',       NULL,                8200000,'3101234067',2, NULL,        NULL,       NULL,       'No contesta'),
    (70000067,9003,90000016,'Promesa de Pago',   'Promesa de Pago',   8200000,'3101234067',4, '2026-04-14','2026-05-01',2733333.00,NULL),
    -- ── 70000068: Sin Gestión en Cartera A → Pago Total en C ────────
    (70000068,9001,90000001,'Número Errado',     NULL,                3500000,'3101234068',1, NULL,        NULL,       NULL,       'Número no corresponde'),
    (70000068,9003,90000017,'Pago Total',        'Pago Total',        3500000,'3101234068',3, '2026-04-07',NULL,       NULL,       NULL),
    -- ── 70000069: Sin Gestión en Cartera A → Acuerdo en C ───────────
    (70000069,9001,90000002,'Sin Gestión',       NULL,                5500000,'3101234069',0, NULL,        NULL,       NULL,       NULL),
    (70000069,9003,90000018,'Acuerdo',           'Acuerdo',           5500000,'3101234069',5, '2026-04-10','2026-05-10',1833333.00,NULL),
    -- ── 70000070: Sin Gestión en Cartera A → Normalizado en C ───────
    (70000070,9001,90000003,'Buzón de voz',      NULL,                2100000,'3101234070',1, NULL,        NULL,       NULL,       'Buzón activo'),
    (70000070,9003,90000019,'Normalizado',       'Normalizado',       2100000,'3101234070',4, '2026-04-11',NULL,       NULL,       NULL),
    -- ── 70000071: Sin Gestión en Cartera B → Contacto Positivo en C ─
    (70000071,9002,90000008,'No Contesta',       NULL,                4100000,'3101234071',1, NULL,        NULL,       NULL,       'No contesta'),
    (70000071,9003,90000015,'Contacto Positivo', 'Contacto Positivo', 4100000,'3101234071',3, '2026-04-15',NULL,       NULL,       NULL),
    -- ── 70000072: Sin Gestión en Cartera B → Promesa de Pago en C ───
    (70000072,9002,90000009,'Buzón de voz',      NULL,                7800000,'3101234072',2, NULL,        NULL,       NULL,       'Buzón activo'),
    (70000072,9003,90000016,'Promesa de Pago',   'Promesa de Pago',   7800000,'3101234072',5, '2026-04-16','2026-04-30',2600000.00,NULL),
    -- ── 70000073: Sin Gestión en Cartera B → Pago Total en C ────────
    (70000073,9002,90000010,'Número Errado',     NULL,                3300000,'3101234073',1, NULL,        NULL,       NULL,       'Número no corresponde'),
    (70000073,9003,90000017,'Pago Total',        'Pago Total',        3300000,'3101234073',4, '2026-04-06',NULL,       NULL,       NULL),
    -- ── 70000074: Sin Gestión en Cartera B → Acuerdo en C ───────────
    (70000074,9002,90000011,'Sin Gestión',       NULL,                6100000,'3101234074',0, NULL,        NULL,       NULL,       NULL),
    (70000074,9003,90000018,'Acuerdo',           'Acuerdo',           6100000,'3101234074',3, '2026-04-13','2026-05-15',2033333.00,NULL),
    -- ── 70000075: Sin Gestión en Cartera B → Paz y Salvo en C ───────
    (70000075,9002,90000012,'Buzón de voz',      NULL,                9200000,'3101234075',1, NULL,        NULL,       NULL,       'Buzón activo'),
    (70000075,9003,90000019,'Paz y Salvo',       'Paz y Salvo',       9200000,'3101234075',6, '2026-04-08',NULL,       NULL,       NULL)
) AS v(tid, cid, aid, up, mp, saldo, tel, rot, fg, fp, vp, motivo)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.UltimoEstadoTitular
    WHERE TitularID = v.tid AND CarteraID = v.cid AND AgenteID = v.aid
);

-- ── 4b. Titulares SinGestión en C con positivo en A/B (70000076–80) ──────────
INSERT INTO dbo.Titular (NumeroDocumento, Nombre)
SELECT v.doc, v.nom FROM (VALUES
    (70000076,'Deudor Comp 16'),(70000077,'Deudor Comp 17'),(70000078,'Deudor Comp 18'),
    (70000079,'Deudor Comp 19'),(70000080,'Deudor Comp 20')
) AS v(doc, nom)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Titular WHERE NumeroDocumento = v.doc);

INSERT INTO dbo.Titular_Cartera (TitularID, LiderID, CarteraID)
SELECT v.t, v.l, v.c FROM (VALUES
    (70000076,9003,9003),(70000076,9001,9001),
    (70000077,9003,9003),(70000077,9001,9001),
    (70000078,9003,9003),(70000078,9001,9001),
    (70000079,9003,9003),(70000079,9002,9002),
    (70000080,9003,9003),(70000080,9002,9002)
) AS v(t, l, c)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Titular_Cartera WHERE TitularID = v.t AND CarteraID = v.c
);

INSERT INTO dbo.UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
     SaldoTotal, NumeroMarcado, Rotacion,
     FechaUltimaGestion, FechaPromesa, ValorPromesa, MotivoNoPago, FechaActualizacion)
SELECT v.tid, v.cid, v.aid, v.up, v.mp,
       v.saldo, v.tel, v.rot,
       CONVERT(date, v.fg, 23), CONVERT(date, v.fp, 23),
       v.vp, v.motivo,
       CONVERT(datetime2, '2026-04-17 08:00:00', 120)
FROM (VALUES
    (70000076,9003,90000015,'No Contesta',       NULL,                4200000,'3101234076',1, NULL,        NULL,        NULL,       'No contesta'),
    (70000076,9001,90000001,'Contacto Positivo','Contacto Positivo',  4200000,'3204571076',2, '2026-04-12',NULL,        NULL,       NULL),
    (70000077,9003,90000016,'No Contesta',       NULL,                6800000,'3101234077',2, NULL,        NULL,        NULL,       'No contesta'),
    (70000077,9001,90000002,'Promesa de Pago',  'Promesa de Pago',    6800000,'3157892077',3, '2026-04-14','2026-05-02',2266666.00, NULL),
    (70000078,9003,90000017,'No Contesta',       NULL,                3100000,'3101234078',1, NULL,        NULL,        NULL,       'No contesta'),
    (70000078,9001,90000003,'Pago Total',        'Pago Total',        3100000,'3187893078',4, '2026-04-09',NULL,        NULL,       NULL),
    (70000079,9003,90000018,'No Contesta',       NULL,                5400000,'3101234079',1, NULL,        NULL,        NULL,       'No contesta'),
    (70000079,9002,90000008,'Acuerdo',           'Acuerdo',           5400000,'3214564079',3, '2026-04-11','2026-05-08',1800000.00, NULL),
    (70000080,9003,90000019,'No Contesta',       NULL,                8900000,'3101234080',2, NULL,        NULL,        NULL,       'No contesta'),
    (70000080,9002,90000009,'Paz y Salvo',       'Paz y Salvo',       8900000,'3194565080',5, '2026-04-10',NULL,        NULL,       NULL)
) AS v(tid, cid, aid, up, mp, saldo, tel, rot, fg, fp, vp, motivo)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.UltimoEstadoTitular
    WHERE TitularID = v.tid AND CarteraID = v.cid AND AgenteID = v.aid
);

GO

-- ── Verificación ──────────────────────────────────────────────
SELECT
    c.NombreCartera,
    COUNT(DISTINCT tc.TitularID) AS Titulares,
    SUM(CASE WHEN ue.UltimoPerfil IN ('Pago Total','Paz y Salvo','Normalizado')       THEN 1 ELSE 0 END) AS YaPago,
    SUM(CASE WHEN ue.UltimoPerfil IN ('Contacto Positivo','Promesa de Pago','Acuerdo') THEN 1 ELSE 0 END) AS ContactoPositivo,
    SUM(CASE WHEN ue.UltimoPerfil NOT IN ('Pago Total','Paz y Salvo','Normalizado',
                                           'Contacto Positivo','Promesa de Pago','Acuerdo')
              OR ue.UltimoPerfil IS NULL THEN 1 ELSE 0 END) AS SinGestion
FROM dbo.Titular_Cartera tc
JOIN dbo.Cartera c ON c.ID = tc.CarteraID
LEFT JOIN dbo.UltimoEstadoTitular ue ON ue.TitularID = tc.TitularID AND ue.CarteraID = tc.CarteraID
WHERE tc.TitularID BETWEEN 70000061 AND 70000080
GROUP BY c.NombreCartera
ORDER BY c.NombreCartera;

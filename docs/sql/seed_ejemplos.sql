-- ============================================================
-- seed_ejemplos.sql — Datos de prueba Zentinela (completo)
-- ============================================================
-- 20 asesores "Ejemplo X" distribuidos en 3 líderes y 3 carteras.
-- Incluye gestiones, pagos abril 2026 y metas para Informes de Recaudo.
--
-- Contraseña de todos los usuarios: Ejemplo@1234
-- Hash: PBKDF2-HMACSHA256, 100 000 iteraciones, sal fija
--
-- Distribución:
--   Cartera Ejemplo A (ID 9001) → Líder Ejemplo 1 → Asesores 1–7
--   Cartera Ejemplo B (ID 9002) → Líder Ejemplo 2 → Asesores 8–14
--   Cartera Ejemplo C (ID 9003) → Líder Ejemplo 3 → Asesores 15–20
--
-- IDs reservados para datos de prueba:
--   Lider:       9001 – 9003
--   Cartera:     9001 – 9003
--   AgenteID:    90000001 – 90000020
--   TitularID:   70000001 – 70000060
--   AspNetUsers: 90001 – 90023
-- ============================================================

-- ============================================================
--  BLOQUE 1 — DBCallCenter
-- ============================================================
USE DBCallCenter;
GO

-- ── 1.1 Líderes ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.Lider WHERE ID = 9001)
    INSERT INTO dbo.Lider (ID, Nombre, Email, Activo, NumeroDocumento)
    VALUES (9001, 'Líder Ejemplo 1', 'lider.ejemplo1@zentinela.com', 1, 80000001);
IF NOT EXISTS (SELECT 1 FROM dbo.Lider WHERE ID = 9002)
    INSERT INTO dbo.Lider (ID, Nombre, Email, Activo, NumeroDocumento)
    VALUES (9002, 'Líder Ejemplo 2', 'lider.ejemplo2@zentinela.com', 1, 80000002);
IF NOT EXISTS (SELECT 1 FROM dbo.Lider WHERE ID = 9003)
    INSERT INTO dbo.Lider (ID, Nombre, Email, Activo, NumeroDocumento)
    VALUES (9003, 'Líder Ejemplo 3', 'lider.ejemplo3@zentinela.com', 1, 80000003);

-- ── 1.2 Carteras ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.Cartera WHERE ID = 9001)
    INSERT INTO dbo.Cartera (ID, NombreCartera, Activa) VALUES (9001, 'Cartera Ejemplo A', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cartera WHERE ID = 9002)
    INSERT INTO dbo.Cartera (ID, NombreCartera, Activa) VALUES (9002, 'Cartera Ejemplo B', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Cartera WHERE ID = 9003)
    INSERT INTO dbo.Cartera (ID, NombreCartera, Activa) VALUES (9003, 'Cartera Ejemplo C', 1);

-- ── 1.3 Lider_Cartera ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = 9001 AND CarteraID = 9001)
    INSERT INTO dbo.Lider_Cartera (LiderID, CarteraID) VALUES (9001, 9001);
IF NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = 9002 AND CarteraID = 9002)
    INSERT INTO dbo.Lider_Cartera (LiderID, CarteraID) VALUES (9002, 9002);
IF NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = 9003 AND CarteraID = 9003)
    INSERT INTO dbo.Lider_Cartera (LiderID, CarteraID) VALUES (9003, 9003);

-- ── 1.4 Titulares (60) ──────────────────────────────────────
INSERT INTO dbo.Titular (NumeroDocumento, Nombre)
SELECT v.doc, v.nom
FROM (VALUES
    (70000001,'Deudor Ejemplo 01'),(70000002,'Deudor Ejemplo 02'),(70000003,'Deudor Ejemplo 03'),
    (70000004,'Deudor Ejemplo 04'),(70000005,'Deudor Ejemplo 05'),(70000006,'Deudor Ejemplo 06'),
    (70000007,'Deudor Ejemplo 07'),(70000008,'Deudor Ejemplo 08'),(70000009,'Deudor Ejemplo 09'),
    (70000010,'Deudor Ejemplo 10'),(70000011,'Deudor Ejemplo 11'),(70000012,'Deudor Ejemplo 12'),
    (70000013,'Deudor Ejemplo 13'),(70000014,'Deudor Ejemplo 14'),(70000015,'Deudor Ejemplo 15'),
    (70000016,'Deudor Ejemplo 16'),(70000017,'Deudor Ejemplo 17'),(70000018,'Deudor Ejemplo 18'),
    (70000019,'Deudor Ejemplo 19'),(70000020,'Deudor Ejemplo 20'),(70000021,'Deudor Ejemplo 21'),
    (70000022,'Deudor Ejemplo 22'),(70000023,'Deudor Ejemplo 23'),(70000024,'Deudor Ejemplo 24'),
    (70000025,'Deudor Ejemplo 25'),(70000026,'Deudor Ejemplo 26'),(70000027,'Deudor Ejemplo 27'),
    (70000028,'Deudor Ejemplo 28'),(70000029,'Deudor Ejemplo 29'),(70000030,'Deudor Ejemplo 30'),
    (70000031,'Deudor Ejemplo 31'),(70000032,'Deudor Ejemplo 32'),(70000033,'Deudor Ejemplo 33'),
    (70000034,'Deudor Ejemplo 34'),(70000035,'Deudor Ejemplo 35'),(70000036,'Deudor Ejemplo 36'),
    (70000037,'Deudor Ejemplo 37'),(70000038,'Deudor Ejemplo 38'),(70000039,'Deudor Ejemplo 39'),
    (70000040,'Deudor Ejemplo 40'),(70000041,'Deudor Ejemplo 41'),(70000042,'Deudor Ejemplo 42'),
    (70000043,'Deudor Ejemplo 43'),(70000044,'Deudor Ejemplo 44'),(70000045,'Deudor Ejemplo 45'),
    (70000046,'Deudor Ejemplo 46'),(70000047,'Deudor Ejemplo 47'),(70000048,'Deudor Ejemplo 48'),
    (70000049,'Deudor Ejemplo 49'),(70000050,'Deudor Ejemplo 50'),(70000051,'Deudor Ejemplo 51'),
    (70000052,'Deudor Ejemplo 52'),(70000053,'Deudor Ejemplo 53'),(70000054,'Deudor Ejemplo 54'),
    (70000055,'Deudor Ejemplo 55'),(70000056,'Deudor Ejemplo 56'),(70000057,'Deudor Ejemplo 57'),
    (70000058,'Deudor Ejemplo 58'),(70000059,'Deudor Ejemplo 59'),(70000060,'Deudor Ejemplo 60')
) AS v(doc, nom)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Titular WHERE NumeroDocumento = v.doc);

-- ── 1.5 Titular_Cartera ─────────────────────────────────────
-- Asesores 1-7  → Cartera 9001 → Titulares 70000001-70000021
-- Asesores 8-14 → Cartera 9002 → Titulares 70000022-70000042
-- Asesores 15-20→ Cartera 9003 → Titulares 70000043-70000060
INSERT INTO dbo.Titular_Cartera (TitularID, LiderID, CarteraID)
SELECT v.t, v.l, v.c
FROM (VALUES
    (70000001,9001,9001),(70000002,9001,9001),(70000003,9001,9001),
    (70000004,9001,9001),(70000005,9001,9001),(70000006,9001,9001),
    (70000007,9001,9001),(70000008,9001,9001),(70000009,9001,9001),
    (70000010,9001,9001),(70000011,9001,9001),(70000012,9001,9001),
    (70000013,9001,9001),(70000014,9001,9001),(70000015,9001,9001),
    (70000016,9001,9001),(70000017,9001,9001),(70000018,9001,9001),
    (70000019,9001,9001),(70000020,9001,9001),(70000021,9001,9001),
    (70000022,9002,9002),(70000023,9002,9002),(70000024,9002,9002),
    (70000025,9002,9002),(70000026,9002,9002),(70000027,9002,9002),
    (70000028,9002,9002),(70000029,9002,9002),(70000030,9002,9002),
    (70000031,9002,9002),(70000032,9002,9002),(70000033,9002,9002),
    (70000034,9002,9002),(70000035,9002,9002),(70000036,9002,9002),
    (70000037,9002,9002),(70000038,9002,9002),(70000039,9002,9002),
    (70000040,9002,9002),(70000041,9002,9002),(70000042,9002,9002),
    (70000043,9003,9003),(70000044,9003,9003),(70000045,9003,9003),
    (70000046,9003,9003),(70000047,9003,9003),(70000048,9003,9003),
    (70000049,9003,9003),(70000050,9003,9003),(70000051,9003,9003),
    (70000052,9003,9003),(70000053,9003,9003),(70000054,9003,9003),
    (70000055,9003,9003),(70000056,9003,9003),(70000057,9003,9003),
    (70000058,9003,9003),(70000059,9003,9003),(70000060,9003,9003)
) AS v(t, l, c)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Titular_Cartera WHERE TitularID = v.t AND CarteraID = v.c);

-- ── 1.6 Agentes (20) ────────────────────────────────────────
INSERT INTO dbo.Agente (AgenteID, UsuarioControlNext, Activo)
SELECT v.id, v.usr, 1
FROM (VALUES
    (90000001,'ejemplo01'),(90000002,'ejemplo02'),(90000003,'ejemplo03'),(90000004,'ejemplo04'),
    (90000005,'ejemplo05'),(90000006,'ejemplo06'),(90000007,'ejemplo07'),(90000008,'ejemplo08'),
    (90000009,'ejemplo09'),(90000010,'ejemplo10'),(90000011,'ejemplo11'),(90000012,'ejemplo12'),
    (90000013,'ejemplo13'),(90000014,'ejemplo14'),(90000015,'ejemplo15'),(90000016,'ejemplo16'),
    (90000017,'ejemplo17'),(90000018,'ejemplo18'),(90000019,'ejemplo19'),(90000020,'ejemplo20')
) AS v(id, usr)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Agente WHERE AgenteID = v.id);

-- ── 1.7 Cartera_Agente ──────────────────────────────────────
INSERT INTO dbo.Cartera_Agente (AgenteID, LiderID, CarteraID)
SELECT v.a, v.l, v.c
FROM (VALUES
    (90000001,9001,9001),(90000002,9001,9001),(90000003,9001,9001),(90000004,9001,9001),
    (90000005,9001,9001),(90000006,9001,9001),(90000007,9001,9001),
    (90000008,9002,9002),(90000009,9002,9002),(90000010,9002,9002),(90000011,9002,9002),
    (90000012,9002,9002),(90000013,9002,9002),(90000014,9002,9002),
    (90000015,9003,9003),(90000016,9003,9003),(90000017,9003,9003),(90000018,9003,9003),
    (90000019,9003,9003),(90000020,9003,9003)
) AS v(a, l, c)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Cartera_Agente WHERE AgenteID = v.a AND LiderID = v.l AND CarteraID = v.c);

-- ── 1.8 UltimoEstadoTitular (60 gestiones) ──────────────────
-- Registro 1: acuerdo/pago  → ContactoPositivo + AcuerdoAbono
-- Registro 2: contacto sin acuerdo → ContactoPositivo
-- Registro 3: varía por patrón     → ContactoPositivo o NoContacto
--
-- Métricas esperadas por asesor:
--   Patrón Incumplido  (1,4,7,10,13,16,19) → Contactabilidad=100%, Efectividad=33%
--   Patrón Ilocalizado/Buzón (resto)        → Contactabilidad=67%,  Efectividad=50%

INSERT INTO dbo.UltimoEstadoTitular
       (TitularID,CarteraID,AgenteID,UltimoPerfil,MejorPerfil,
        SaldoTotal,NumeroMarcado,Rotacion,FechaUltimaGestion,FechaPromesa,ValorPromesa,MotivoNoPago,FechaActualizacion)
SELECT v.tid,v.cid,v.aid,v.up,v.mp,
       v.saldo,v.tel,v.rot,
       CONVERT(date,'2026-04-17',23),CONVERT(date,v.fp,23),v.vp,v.motivo,
       CONVERT(datetime2,'2026-04-17 08:00:00',120)
FROM (VALUES
-- ── Asesor 1 · Cartera 9001 · Incumplido ──────────────────────────────────────
(70000001,9001,90000001,'Acuerdo de pago abono','Acuerdo de pago abono',5400000,'3001234001',3,'2026-04-25',1800000.00,NULL),
(70000002,9001,90000001,'No hubo acuerdo','No hubo acuerdo',5400000,'3001234001',3,NULL,NULL,NULL),
(70000003,9001,90000001,'Incumplido','No hubo acuerdo',5400000,'3001234001',3,NULL,NULL,NULL),
-- ── Asesor 2 · Cartera 9001 · Ilocalizado ─────────────────────────────────────
(70000004,9001,90000002,'Acuerdo de pago total','Acuerdo de pago total',7500000,'3001234002',5,'2026-04-30',2500000.00,NULL),
(70000005,9001,90000002,'Renuente','Renuente',7500000,'3001234002',5,NULL,NULL,NULL),
(70000006,9001,90000002,'Ilocalizado',NULL,7500000,'3001234002',5,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 3 · Cartera 9001 · Mensaje en buzón ────────────────────────────────
(70000007,9001,90000003,'Realizo abono','Realizo abono',2850000,'3001234003',2,'2026-05-05',950000.00,NULL),
(70000008,9001,90000003,'Potencial acuerdo','Potencial acuerdo',2850000,'3001234003',2,NULL,NULL,NULL),
(70000009,9001,90000003,'Mensaje en buzón',NULL,2850000,'3001234003',2,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 4 · Cartera 9001 · Incumplido ──────────────────────────────────────
(70000010,9001,90000004,'Paz y salvo','Paz y salvo',9600000,'3001234004',4,'2026-04-22',3200000.00,NULL),
(70000011,9001,90000004,'No hubo acuerdo','No hubo acuerdo',9600000,'3001234004',4,NULL,NULL,NULL),
(70000012,9001,90000004,'Incumplido','No hubo acuerdo',9600000,'3001234004',4,NULL,NULL,NULL),
-- ── Asesor 5 · Cartera 9001 · Ilocalizado ─────────────────────────────────────
(70000013,9001,90000005,'Acuerdo de pago abono','Acuerdo de pago abono',4500000,'3001234005',6,'2026-05-10',1500000.00,NULL),
(70000014,9001,90000005,'No hubo acuerdo','No hubo acuerdo',4500000,'3001234005',6,NULL,NULL,NULL),
(70000015,9001,90000005,'Ilocalizado',NULL,4500000,'3001234005',6,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 6 · Cartera 9001 · Mensaje en buzón ────────────────────────────────
(70000016,9001,90000006,'Acuerdo de pago total','Acuerdo de pago total',12300000,'3001234006',3,'2026-04-27',4100000.00,NULL),
(70000017,9001,90000006,'Renuente','Renuente',12300000,'3001234006',3,NULL,NULL,NULL),
(70000018,9001,90000006,'Mensaje en buzón',NULL,12300000,'3001234006',3,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 7 · Cartera 9001 · Incumplido ──────────────────────────────────────
(70000019,9001,90000007,'Realizo abono','Realizo abono',2340000,'3001234007',7,'2026-05-03',780000.00,NULL),
(70000020,9001,90000007,'Potencial acuerdo','Potencial acuerdo',2340000,'3001234007',7,NULL,NULL,NULL),
(70000021,9001,90000007,'Incumplido','Potencial acuerdo',2340000,'3001234007',7,NULL,NULL,NULL),
-- ── Asesor 8 · Cartera 9002 · Ilocalizado ─────────────────────────────────────
(70000022,9002,90000008,'Paz y salvo','Paz y salvo',8400000,'3001234008',4,'2026-04-24',2800000.00,NULL),
(70000023,9002,90000008,'No hubo acuerdo','No hubo acuerdo',8400000,'3001234008',4,NULL,NULL,NULL),
(70000024,9002,90000008,'Ilocalizado',NULL,8400000,'3001234008',4,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 9 · Cartera 9002 · Mensaje en buzón ────────────────────────────────
(70000025,9002,90000009,'Acuerdo de pago abono','Acuerdo de pago abono',3750000,'3001234009',2,'2026-04-29',1250000.00,NULL),
(70000026,9002,90000009,'No hubo acuerdo','No hubo acuerdo',3750000,'3001234009',2,NULL,NULL,NULL),
(70000027,9002,90000009,'Mensaje en buzón',NULL,3750000,'3001234009',2,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 10 · Cartera 9002 · Incumplido ─────────────────────────────────────
(70000028,9002,90000010,'Acuerdo de pago total','Acuerdo de pago total',10800000,'3001234010',5,'2026-05-08',3600000.00,NULL),
(70000029,9002,90000010,'Renuente','Renuente',10800000,'3001234010',5,NULL,NULL,NULL),
(70000030,9002,90000010,'Incumplido','Renuente',10800000,'3001234010',5,NULL,NULL,NULL),
-- ── Asesor 11 · Cartera 9002 · Ilocalizado ────────────────────────────────────
(70000031,9002,90000011,'Realizo abono','Realizo abono',2010000,'3001234011',3,'2026-04-21',670000.00,NULL),
(70000032,9002,90000011,'Potencial acuerdo','Potencial acuerdo',2010000,'3001234011',3,NULL,NULL,NULL),
(70000033,9002,90000011,'Ilocalizado',NULL,2010000,'3001234011',3,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 12 · Cartera 9002 · Mensaje en buzón ───────────────────────────────
(70000034,9002,90000012,'Paz y salvo','Paz y salvo',14700000,'3001234012',6,'2026-05-01',4900000.00,NULL),
(70000035,9002,90000012,'No hubo acuerdo','No hubo acuerdo',14700000,'3001234012',6,NULL,NULL,NULL),
(70000036,9002,90000012,'Mensaje en buzón',NULL,14700000,'3001234012',6,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 13 · Cartera 9002 · Incumplido ─────────────────────────────────────
(70000037,9002,90000013,'Acuerdo de pago abono','Acuerdo de pago abono',5850000,'3001234013',4,'2026-04-26',1950000.00,NULL),
(70000038,9002,90000013,'No hubo acuerdo','No hubo acuerdo',5850000,'3001234013',4,NULL,NULL,NULL),
(70000039,9002,90000013,'Incumplido','No hubo acuerdo',5850000,'3001234013',4,NULL,NULL,NULL),
-- ── Asesor 14 · Cartera 9002 · Ilocalizado ────────────────────────────────────
(70000040,9002,90000014,'Acuerdo de pago total','Acuerdo de pago total',6900000,'3001234014',3,'2026-04-28',2300000.00,NULL),
(70000041,9002,90000014,'Renuente','Renuente',6900000,'3001234014',3,NULL,NULL,NULL),
(70000042,9002,90000014,'Ilocalizado',NULL,6900000,'3001234014',3,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 15 · Cartera 9003 · Mensaje en buzón ───────────────────────────────
(70000043,9003,90000015,'Realizo abono','Realizo abono',2460000,'3001234015',5,'2026-05-06',820000.00,NULL),
(70000044,9003,90000015,'Potencial acuerdo','Potencial acuerdo',2460000,'3001234015',5,NULL,NULL,NULL),
(70000045,9003,90000015,'Mensaje en buzón',NULL,2460000,'3001234015',5,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 16 · Cartera 9003 · Incumplido ─────────────────────────────────────
(70000046,9003,90000016,'Paz y salvo','Paz y salvo',11400000,'3001234016',2,'2026-04-23',3800000.00,NULL),
(70000047,9003,90000016,'No hubo acuerdo','No hubo acuerdo',11400000,'3001234016',2,NULL,NULL,NULL),
(70000048,9003,90000016,'Incumplido','No hubo acuerdo',11400000,'3001234016',2,NULL,NULL,NULL),
-- ── Asesor 17 · Cartera 9003 · Ilocalizado ────────────────────────────────────
(70000049,9003,90000017,'Acuerdo de pago abono','Acuerdo de pago abono',4200000,'3001234017',7,'2026-05-09',1400000.00,NULL),
(70000050,9003,90000017,'No hubo acuerdo','No hubo acuerdo',4200000,'3001234017',7,NULL,NULL,NULL),
(70000051,9003,90000017,'Ilocalizado',NULL,4200000,'3001234017',7,NULL,NULL,'Sin número de contacto'),
-- ── Asesor 18 · Cartera 9003 · Mensaje en buzón ───────────────────────────────
(70000052,9003,90000018,'Acuerdo de pago total','Acuerdo de pago total',8100000,'3001234018',4,'2026-04-30',2700000.00,NULL),
(70000053,9003,90000018,'Renuente','Renuente',8100000,'3001234018',4,NULL,NULL,NULL),
(70000054,9003,90000018,'Mensaje en buzón',NULL,8100000,'3001234018',4,NULL,NULL,'Buzón de voz activo'),
-- ── Asesor 19 · Cartera 9003 · Incumplido ─────────────────────────────────────
(70000055,9003,90000019,'Realizo abono','Realizo abono',1890000,'3001234019',3,'2026-04-21',630000.00,NULL),
(70000056,9003,90000019,'Potencial acuerdo','Potencial acuerdo',1890000,'3001234019',3,NULL,NULL,NULL),
(70000057,9003,90000019,'Incumplido','Potencial acuerdo',1890000,'3001234019',3,NULL,NULL,NULL),
-- ── Asesor 20 · Cartera 9003 · Ilocalizado ────────────────────────────────────
(70000058,9003,90000020,'Paz y salvo','Paz y salvo',12900000,'3001234020',6,'2026-05-04',4300000.00,NULL),
(70000059,9003,90000020,'No hubo acuerdo','No hubo acuerdo',12900000,'3001234020',6,NULL,NULL,NULL),
(70000060,9003,90000020,'Ilocalizado',NULL,12900000,'3001234020',6,NULL,NULL,'Sin número de contacto')
) AS v(tid,cid,aid,up,mp,saldo,tel,rot,fp,vp,motivo)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.UltimoEstadoTitular
    WHERE TitularID = v.tid AND CarteraID = v.cid AND AgenteID = v.aid
);

-- ── 1.9 Pagos — abril 2026 (2-3 pagos por asesor) ──────────
-- UsuarioRegistro = UserName del líder; v.usr trae su ID en DBZentinela.AspNetUsers
-- y se traduce a UserName en el SELECT.
--   Cartera 9001 → 90021 · Cartera 9002 → 90022 · Cartera 9003 → 90023
-- Honorarios = 10% del ValorPagado (calculado en el INSERT)

INSERT INTO dbo.Pago
    (CedulaTitular, NombreCliente, ValorPagado, FechaPago,
     Honorarios, LiderID, CarteraID, UsuarioRegistro, AgenteControlID)
SELECT v.ced, v.nom, v.valor, CONVERT(date,v.fecha,23), ROUND(v.valor * 0.10, 0), v.lid, v.car,
       (SELECT u.UserName FROM DBZentinela.dbo.AspNetUsers u WHERE u.Id = v.usr), v.age
FROM (VALUES
    -- Asesor 1
    (70000001,'Deudor Ejemplo 01', 900000.00,'2026-04-05',9001,9001,90021,90000001),
    (70000001,'Deudor Ejemplo 01', 900000.00,'2026-04-15',9001,9001,90021,90000001),
    -- Asesor 2
    (70000004,'Deudor Ejemplo 04',1250000.00,'2026-04-07',9001,9001,90021,90000002),
    (70000004,'Deudor Ejemplo 04',1250000.00,'2026-04-14',9001,9001,90021,90000002),
    -- Asesor 3
    (70000007,'Deudor Ejemplo 07', 475000.00,'2026-04-08',9001,9001,90021,90000003),
    (70000007,'Deudor Ejemplo 07', 475000.00,'2026-04-16',9001,9001,90021,90000003),
    -- Asesor 4
    (70000010,'Deudor Ejemplo 10',1600000.00,'2026-04-05',9001,9001,90021,90000004),
    (70000010,'Deudor Ejemplo 10',1600000.00,'2026-04-12',9001,9001,90021,90000004),
    -- Asesor 5
    (70000013,'Deudor Ejemplo 13', 750000.00,'2026-04-10',9001,9001,90021,90000005),
    (70000013,'Deudor Ejemplo 13', 750000.00,'2026-04-17',9001,9001,90021,90000005),
    -- Asesor 6
    (70000016,'Deudor Ejemplo 16',2050000.00,'2026-04-06',9001,9001,90021,90000006),
    (70000016,'Deudor Ejemplo 16',2050000.00,'2026-04-13',9001,9001,90021,90000006),
    -- Asesor 7
    (70000019,'Deudor Ejemplo 19', 390000.00,'2026-04-09',9001,9001,90021,90000007),
    (70000019,'Deudor Ejemplo 19', 390000.00,'2026-04-16',9001,9001,90021,90000007),
    -- Asesor 8
    (70000022,'Deudor Ejemplo 22', 933000.00,'2026-04-05',9002,9002,90022,90000008),
    (70000022,'Deudor Ejemplo 22', 933000.00,'2026-04-11',9002,9002,90022,90000008),
    (70000022,'Deudor Ejemplo 22', 934000.00,'2026-04-17',9002,9002,90022,90000008),
    -- Asesor 9
    (70000025,'Deudor Ejemplo 25', 625000.00,'2026-04-08',9002,9002,90022,90000009),
    (70000025,'Deudor Ejemplo 25', 625000.00,'2026-04-15',9002,9002,90022,90000009),
    -- Asesor 10
    (70000028,'Deudor Ejemplo 28',1200000.00,'2026-04-06',9002,9002,90022,90000010),
    (70000028,'Deudor Ejemplo 28',1200000.00,'2026-04-12',9002,9002,90022,90000010),
    (70000028,'Deudor Ejemplo 28',1200000.00,'2026-04-17',9002,9002,90022,90000010),
    -- Asesor 11
    (70000031,'Deudor Ejemplo 31', 335000.00,'2026-04-09',9002,9002,90022,90000011),
    (70000031,'Deudor Ejemplo 31', 335000.00,'2026-04-16',9002,9002,90022,90000011),
    -- Asesor 12
    (70000034,'Deudor Ejemplo 34',1633000.00,'2026-04-07',9002,9002,90022,90000012),
    (70000034,'Deudor Ejemplo 34',1633000.00,'2026-04-14',9002,9002,90022,90000012),
    (70000034,'Deudor Ejemplo 34',1634000.00,'2026-04-17',9002,9002,90022,90000012),
    -- Asesor 13
    (70000037,'Deudor Ejemplo 37', 975000.00,'2026-04-10',9002,9002,90022,90000013),
    (70000037,'Deudor Ejemplo 37', 975000.00,'2026-04-17',9002,9002,90022,90000013),
    -- Asesor 14
    (70000040,'Deudor Ejemplo 40',1150000.00,'2026-04-05',9002,9002,90022,90000014),
    (70000040,'Deudor Ejemplo 40',1150000.00,'2026-04-13',9002,9002,90022,90000014),
    -- Asesor 15
    (70000043,'Deudor Ejemplo 43', 410000.00,'2026-04-08',9003,9003,90023,90000015),
    (70000043,'Deudor Ejemplo 43', 410000.00,'2026-04-15',9003,9003,90023,90000015),
    -- Asesor 16
    (70000046,'Deudor Ejemplo 46',1266000.00,'2026-04-06',9003,9003,90023,90000016),
    (70000046,'Deudor Ejemplo 46',1267000.00,'2026-04-12',9003,9003,90023,90000016),
    (70000046,'Deudor Ejemplo 46',1267000.00,'2026-04-17',9003,9003,90023,90000016),
    -- Asesor 17
    (70000049,'Deudor Ejemplo 49', 700000.00,'2026-04-09',9003,9003,90023,90000017),
    (70000049,'Deudor Ejemplo 49', 700000.00,'2026-04-16',9003,9003,90023,90000017),
    -- Asesor 18
    (70000052,'Deudor Ejemplo 52', 900000.00,'2026-04-07',9003,9003,90023,90000018),
    (70000052,'Deudor Ejemplo 52', 900000.00,'2026-04-14',9003,9003,90023,90000018),
    (70000052,'Deudor Ejemplo 52', 900000.00,'2026-04-17',9003,9003,90023,90000018),
    -- Asesor 19
    (70000055,'Deudor Ejemplo 55', 315000.00,'2026-04-10',9003,9003,90023,90000019),
    (70000055,'Deudor Ejemplo 55', 315000.00,'2026-04-17',9003,9003,90023,90000019),
    -- Asesor 20
    (70000058,'Deudor Ejemplo 58',1433000.00,'2026-04-05',9003,9003,90023,90000020),
    (70000058,'Deudor Ejemplo 58',1433000.00,'2026-04-11',9003,9003,90023,90000020),
    (70000058,'Deudor Ejemplo 58',1434000.00,'2026-04-16',9003,9003,90023,90000020)
) AS v(ced, nom, valor, fecha, lid, car, usr, age)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Pago
    WHERE AgenteControlID = v.age AND CedulaTitular = v.ced AND FechaPago = CONVERT(date,v.fecha,23)
);

-- ── 1.10 Metas por agente — abril 2026 ──────────────────────
-- Meta ≈ 130% del recaudo real → cumplimiento esperado ~75%
INSERT INTO dbo.Meta_Agente_Mes (AgenteID, Anio, Mes, MetaRecaudo, MetaHonorarios)
SELECT v.age, 2026, 4, v.meta, v.metahon
FROM (VALUES
    (90000001, 2500000.00,  250000.00),(90000002, 3500000.00,  350000.00),(90000003, 1300000.00,  130000.00),
    (90000004, 4300000.00,  430000.00),(90000005, 2000000.00,  200000.00),(90000006, 5700000.00,  570000.00),
    (90000007, 1100000.00,  110000.00),(90000008, 3700000.00,  370000.00),(90000009, 1750000.00,  175000.00),
    (90000010, 4700000.00,  470000.00),(90000011,  950000.00,   95000.00),(90000012, 6600000.00,  660000.00),
    (90000013, 2700000.00,  270000.00),(90000014, 3200000.00,  320000.00),(90000015, 1100000.00,  110000.00),
    (90000016, 5100000.00,  510000.00),(90000017, 1900000.00,  190000.00),(90000018, 3600000.00,  360000.00),
    (90000019,  870000.00,   87000.00),(90000020, 5900000.00,  590000.00)
) AS v(age, meta, metahon)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Meta_Agente_Mes
    WHERE AgenteID = v.age AND Anio = 2026 AND Mes = 4
);

-- ── 1.11 Metas por líder-cartera — abril 2026 ───────────────
-- Líder 1 recauda ≈ 19.6M → meta 25M
-- Líder 2 recauda ≈ 22.3M → meta 29M
-- Líder 3 recauda ≈ 17.5M → meta 23M
INSERT INTO dbo.Meta_Lider_Cartera_Mes (LiderID, CarteraID, Anio, Mes, MetaRecaudo, MetaHonorarios)
SELECT v.lid, v.car, 2026, 4, v.meta, v.metahon
FROM (VALUES
    (9001,9001,25000000.00,2500000.00),
    (9002,9002,29000000.00,2900000.00),
    (9003,9003,23000000.00,2300000.00)
) AS v(lid, car, meta, metahon)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Meta_Lider_Cartera_Mes
    WHERE LiderID = v.lid AND CarteraID = v.car AND Anio = 2026 AND Mes = 4
);

GO

-- ============================================================
--  BLOQUE 2 — DBRecursosHumanos
--  Empleados para que el dashboard muestre nombres "Ejemplo X"
-- ============================================================
USE DBRecursosHumanos;
GO

-- NOTA: Ajusta AreaID si el ID 1 no existe en tu tabla de áreas.
IF NOT EXISTS (SELECT 1 FROM dbo.Cargo WHERE ID = 9000)
BEGIN
    SET IDENTITY_INSERT dbo.Cargo ON;
    INSERT INTO dbo.Cargo (ID, Descripcion, AreaID) VALUES (9000, 'Asesor de Cobranza Ejemplo', 1);
    SET IDENTITY_INSERT dbo.Cargo OFF;
END;

SET IDENTITY_INSERT dbo.Empleado ON;
INSERT INTO dbo.Empleado
    (ID, NumeroDocumento, TipoDocumento, Nombre, Apellidos, FechaNacimiento,
     Sexo, Ciudad, CargoID, EmpresaID, EstadoEmpleado)
SELECT v.id, v.doc, 1, 'Ejemplo', v.ap, '1990-01-01', 'M', 'Barranquilla', 9000, 1, 1
FROM (VALUES
    (9001,90000001,'1'),(9002,90000002,'2'),(9003,90000003,'3'),(9004,90000004,'4'),
    (9005,90000005,'5'),(9006,90000006,'6'),(9007,90000007,'7'),(9008,90000008,'8'),
    (9009,90000009,'9'),(9010,90000010,'10'),(9011,90000011,'11'),(9012,90000012,'12'),
    (9013,90000013,'13'),(9014,90000014,'14'),(9015,90000015,'15'),(9016,90000016,'16'),
    (9017,90000017,'17'),(9018,90000018,'18'),(9019,90000019,'19'),(9020,90000020,'20')
) AS v(id, doc, ap)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Empleado WHERE NumeroDocumento = v.doc);
SET IDENTITY_INSERT dbo.Empleado OFF;

GO

-- ============================================================
--  BLOQUE 3 — DBZentinela (Identity)
--  Usuarios para login de asesores y líderes.
--  Contraseña: Ejemplo@1234
-- ============================================================
USE DBZentinela;
GO

DECLARE @hash NVARCHAR(MAX) = N'AQAAAAEAAYagAAAAEFplbnRpbmVsYUZpam8xMiGs0TExE1zoTlbe/QGG1DqlsU1Oncq8kUuRO0dZbTHiBw==';

-- ── 3.1 Roles ────────────────────────────────────────────────
INSERT INTO dbo.AspNetRoles (Name, NormalizedName, ConcurrencyStamp)
SELECT v.n, v.nn, NEWID()
FROM (VALUES ('Asesor','ASESOR'),('Lider','LIDER'),('Director','DIRECTOR'),('Admin','ADMIN')) AS v(n,nn)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = v.nn);

-- ── 3.2 Usuarios ────────────────────────────────────────────
SET IDENTITY_INSERT dbo.AspNetUsers ON;

INSERT INTO dbo.AspNetUsers
    (Id, LiderID, NombreCompleto, NumeroDocumento, RequiereCambioPassword,
     UserName, NormalizedUserName, Email, NormalizedEmail,
     EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
     PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
SELECT v.id, v.lid, v.nc, v.doc, 0,
       v.usr, UPPER(v.usr), v.usr, UPPER(v.usr),
       1, @hash, CAST(NEWID() AS NVARCHAR(MAX)), CAST(NEWID() AS NVARCHAR(MAX)),
       0, 0, 1, 0
FROM (VALUES
    (90001,NULL,'Ejemplo 1', 90000001,'ejemplo1@zentinela.com'),
    (90002,NULL,'Ejemplo 2', 90000002,'ejemplo2@zentinela.com'),
    (90003,NULL,'Ejemplo 3', 90000003,'ejemplo3@zentinela.com'),
    (90004,NULL,'Ejemplo 4', 90000004,'ejemplo4@zentinela.com'),
    (90005,NULL,'Ejemplo 5', 90000005,'ejemplo5@zentinela.com'),
    (90006,NULL,'Ejemplo 6', 90000006,'ejemplo6@zentinela.com'),
    (90007,NULL,'Ejemplo 7', 90000007,'ejemplo7@zentinela.com'),
    (90008,NULL,'Ejemplo 8', 90000008,'ejemplo8@zentinela.com'),
    (90009,NULL,'Ejemplo 9', 90000009,'ejemplo9@zentinela.com'),
    (90010,NULL,'Ejemplo 10',90000010,'ejemplo10@zentinela.com'),
    (90011,NULL,'Ejemplo 11',90000011,'ejemplo11@zentinela.com'),
    (90012,NULL,'Ejemplo 12',90000012,'ejemplo12@zentinela.com'),
    (90013,NULL,'Ejemplo 13',90000013,'ejemplo13@zentinela.com'),
    (90014,NULL,'Ejemplo 14',90000014,'ejemplo14@zentinela.com'),
    (90015,NULL,'Ejemplo 15',90000015,'ejemplo15@zentinela.com'),
    (90016,NULL,'Ejemplo 16',90000016,'ejemplo16@zentinela.com'),
    (90017,NULL,'Ejemplo 17',90000017,'ejemplo17@zentinela.com'),
    (90018,NULL,'Ejemplo 18',90000018,'ejemplo18@zentinela.com'),
    (90019,NULL,'Ejemplo 19',90000019,'ejemplo19@zentinela.com'),
    (90020,NULL,'Ejemplo 20',90000020,'ejemplo20@zentinela.com'),
    (90021,9001,'Líder Ejemplo 1',80000001,'lider.ejemplo1@zentinela.com'),
    (90022,9002,'Líder Ejemplo 2',80000002,'lider.ejemplo2@zentinela.com'),
    (90023,9003,'Líder Ejemplo 3',80000003,'lider.ejemplo3@zentinela.com')
) AS v(id, lid, nc, doc, usr)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = v.id);

SET IDENTITY_INSERT dbo.AspNetUsers OFF;

-- ── 3.3 Roles de usuario ────────────────────────────────────
INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.AspNetUsers u
CROSS JOIN dbo.AspNetRoles r
WHERE u.Id BETWEEN 90001 AND 90020
  AND r.NormalizedName = 'ASESOR'
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = u.Id AND RoleId = r.Id);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.AspNetUsers u
CROSS JOIN dbo.AspNetRoles r
WHERE u.Id BETWEEN 90021 AND 90023
  AND r.NormalizedName = 'LIDER'
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = u.Id AND RoleId = r.Id);

GO

-- ============================================================
-- VERIFICACIÓN (descomentar para confirmar)
-- ============================================================
/*
USE DBCallCenter;
SELECT COUNT(*) AS Lideres   FROM dbo.Lider                WHERE ID BETWEEN 9001 AND 9003;
SELECT COUNT(*) AS Carteras  FROM dbo.Cartera               WHERE ID BETWEEN 9001 AND 9003;
SELECT COUNT(*) AS Titulares FROM dbo.Titular               WHERE NumeroDocumento BETWEEN 70000001 AND 70000060;
SELECT COUNT(*) AS Agentes   FROM dbo.Agente                WHERE AgenteID BETWEEN 90000001 AND 90000020;
SELECT COUNT(*) AS Gestiones FROM dbo.UltimoEstadoTitular   WHERE AgenteID BETWEEN 90000001 AND 90000020;
SELECT COUNT(*) AS Pagos     FROM dbo.Pago                  WHERE AgenteControlID BETWEEN 90000001 AND 90000020;
SELECT COUNT(*) AS MetasAge  FROM dbo.Meta_Agente_Mes        WHERE AgenteID BETWEEN 90000001 AND 90000020 AND Anio=2026 AND Mes=4;
SELECT COUNT(*) AS MetasLid  FROM dbo.Meta_Lider_Cartera_Mes WHERE LiderID BETWEEN 9001 AND 9003 AND Anio=2026 AND Mes=4;

-- Recaudo por líder vs meta (Informes de Recaudo):
SELECT l.Nombre AS Lider,
       SUM(p.ValorPagado)      AS Recaudo,
       MAX(m.MetaRecaudo)      AS Meta,
       ROUND(SUM(p.ValorPagado)/MAX(m.MetaRecaudo)*100,1) AS PctCumplimiento
FROM dbo.Pago p
JOIN dbo.Meta_Lider_Cartera_Mes m ON m.LiderID=p.LiderID AND m.CarteraID=p.CarteraID AND m.Anio=2026 AND m.Mes=4
JOIN dbo.Lider l ON l.ID = p.LiderID
WHERE p.AgenteControlID BETWEEN 90000001 AND 90000020
GROUP BY l.Nombre, l.ID ORDER BY l.Nombre;
*/

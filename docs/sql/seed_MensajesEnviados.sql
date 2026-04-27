-- ============================================================
-- Datos de prueba — MensajesEnviados
-- Genera registros distribuidos en varios períodos para probar
-- el informe de Mensajería (preset Hoy / Este mes / Mes pasado /
-- Últimos 7 días / Últimos 30 días / Personalizado).
--
-- Marca todos los registros con Canal = 'SEED-TEST' para que el
-- bloque de ROLLBACK al final pueda eliminarlos sin tocar datos
-- reales.
--
-- Requiere que las combinaciones (LiderID, CarteraID) usadas
-- existan en dbo.Lider_Cartera (FK compuesta).
--
-- Ejecutar en: DBCallCenter
-- ============================================================

SET NOCOUNT ON;

-- ────────────────────────────────────────────────────────────
-- Combinaciones Líder + Cartera destino
-- Ajusta los IDs si tu BD tiene otros valores.
-- Verifica antes de correr:
--   SELECT TOP 5 LiderID, CarteraID FROM dbo.Lider_Cartera;
-- ────────────────────────────────────────────────────────────
DECLARE @Lider1_ID      INT          = 1;
DECLARE @Cartera1_ID    INT          = 13;
DECLARE @Cartera1_Nom   NVARCHAR(100) = N'PELICANO PROPIA';

DECLARE @Lider2_ID      INT          = 1;
DECLARE @Cartera2_ID    INT          = 20;
DECLARE @Cartera2_Nom   NVARCHAR(100) = N'LUMEN';

DECLARE @Lider3_ID      INT          = 2;
DECLARE @Cartera3_ID    INT          = 21;
DECLARE @Cartera3_Nom   NVARCHAR(100) = N'MERCADEOS PERSONAL ALMACEN';

-- Validación rápida: detiene el script si las combinaciones no existen
IF NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = @Lider1_ID AND CarteraID = @Cartera1_ID)
   OR NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = @Lider2_ID AND CarteraID = @Cartera2_ID)
   OR NOT EXISTS (SELECT 1 FROM dbo.Lider_Cartera WHERE LiderID = @Lider3_ID AND CarteraID = @Cartera3_ID)
BEGIN
    RAISERROR('Una o más combinaciones (LiderID, CarteraID) no existen en Lider_Cartera. Ajusta los IDs en este script.', 16, 1);
    RETURN;
END

-- ────────────────────────────────────────────────────────────
-- Fechas de referencia
-- ────────────────────────────────────────────────────────────
DECLARE @Hoy           DATE = CAST(GETDATE() AS DATE);
DECLARE @InicioMes     DATE = DATEFROMPARTS(YEAR(@Hoy), MONTH(@Hoy), 1);
DECLARE @InicioMesPrev DATE = DATEADD(MONTH, -1, @InicioMes);
DECLARE @FinMesPrev    DATE = DATEADD(DAY, -1, @InicioMes);
DECLARE @Hace7         DATE = DATEADD(DAY, -6,  @Hoy);
DECLARE @Hace30        DATE = DATEADD(DAY, -29, @Hoy);
DECLARE @Hace60        DATE = DATEADD(DAY, -59, @Hoy);

-- ────────────────────────────────────────────────────────────
-- 1) HOY — 8 mensajes, una línea con 5 envíos para alerta (>4)
-- ────────────────────────────────────────────────────────────
INSERT INTO dbo.MensajesEnviados
    (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
VALUES
    (1010101001, '3001110001', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101002, '3001110002', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101003, '3001110003', @Lider2_ID, @Cartera2_ID, @Hoy, 'SEED-TEST'),
    -- línea con >4 envíos (alerta)
    (1010101010, '3009999999', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101010, '3009999999', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101010, '3009999999', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101010, '3009999999', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST'),
    (1010101010, '3009999999', @Lider1_ID, @Cartera1_ID, @Hoy, 'SEED-TEST');

-- ────────────────────────────────────────────────────────────
-- 2) ESTE MES — 12 mensajes
-- ────────────────────────────────────────────────────────────
DECLARE @i INT = 0;
WHILE @i < 12
BEGIN
    INSERT INTO dbo.MensajesEnviados
        (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
    VALUES
        (1010102000 + @i,
         CONCAT('30022', RIGHT('00000' + CAST(@i AS VARCHAR), 5)),
         CASE WHEN @i % 2 = 0 THEN @Lider1_ID   ELSE @Lider2_ID   END,
         CASE WHEN @i % 2 = 0 THEN @Cartera1_ID ELSE @Cartera2_ID END,
         DATEADD(DAY, @i % DAY(@Hoy), @InicioMes),
         'SEED-TEST');
    SET @i += 1;
END

-- ────────────────────────────────────────────────────────────
-- 3) MES PASADO — 10 mensajes en las 3 combinaciones
-- ────────────────────────────────────────────────────────────
SET @i = 0;
WHILE @i < 10
BEGIN
    INSERT INTO dbo.MensajesEnviados
        (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
    VALUES
        (1010103000 + @i,
         CONCAT('30033', RIGHT('00000' + CAST(@i AS VARCHAR), 5)),
         CASE WHEN @i % 3 = 0 THEN @Lider3_ID
              WHEN @i % 3 = 1 THEN @Lider1_ID
              ELSE @Lider2_ID END,
         CASE WHEN @i % 3 = 0 THEN @Cartera3_ID
              WHEN @i % 3 = 1 THEN @Cartera1_ID
              ELSE @Cartera2_ID END,
         DATEADD(DAY, @i * 2, @InicioMesPrev),
         'SEED-TEST');
    SET @i += 1;
END

-- ────────────────────────────────────────────────────────────
-- 4) ÚLTIMOS 7 DÍAS — 8 mensajes
-- ────────────────────────────────────────────────────────────
SET @i = 0;
WHILE @i < 8
BEGIN
    INSERT INTO dbo.MensajesEnviados
        (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
    VALUES
        (1010104000 + @i,
         CONCAT('30044', RIGHT('00000' + CAST(@i AS VARCHAR), 5)),
         @Lider2_ID, @Cartera2_ID,
         DATEADD(DAY, @i % 7, @Hace7),
         'SEED-TEST');
    SET @i += 1;
END

-- ────────────────────────────────────────────────────────────
-- 5) ÚLTIMOS 30 DÍAS — 15 mensajes
-- ────────────────────────────────────────────────────────────
SET @i = 0;
WHILE @i < 15
BEGIN
    INSERT INTO dbo.MensajesEnviados
        (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
    VALUES
        (1010105000 + @i,
         CONCAT('30055', RIGHT('00000' + CAST(@i AS VARCHAR), 5)),
         @Lider3_ID, @Cartera3_ID,
         DATEADD(DAY, 8 + (@i % 20), @Hace30),
         'SEED-TEST');
    SET @i += 1;
END

-- ────────────────────────────────────────────────────────────
-- 6) HISTÓRICO ANTIGUO (~hace 60 días) — 5 mensajes
-- ────────────────────────────────────────────────────────────
SET @i = 0;
WHILE @i < 5
BEGIN
    INSERT INTO dbo.MensajesEnviados
        (NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal)
    VALUES
        (1010106000 + @i,
         CONCAT('30066', RIGHT('00000' + CAST(@i AS VARCHAR), 5)),
         @Lider1_ID, @Cartera1_ID,
         DATEADD(DAY, @i * 3, @Hace60),
         'SEED-TEST');
    SET @i += 1;
END

PRINT '--- MensajesEnviados: insertados registros con Canal = SEED-TEST ---';
SELECT
    Canal,
    COUNT(*)            AS TotalFilas,
    MIN(FechaEnvio)     AS FechaMin,
    MAX(FechaEnvio)     AS FechaMax,
    COUNT(DISTINCT CarteraID) AS CarterasDistintas
FROM dbo.MensajesEnviados
WHERE Canal = 'SEED-TEST'
GROUP BY Canal;

-- ============================================================
-- ROLLBACK — Borrado de los registros de prueba
-- Para limpiar, ejecuta SOLO el bloque de abajo (descomentado).
-- ============================================================

/*
DELETE FROM dbo.MensajesEnviados WHERE Canal = 'SEED-TEST';
PRINT '--- MensajesEnviados: registros SEED-TEST eliminados ---';
*/

USE EtlHarness;
GO
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.Resultado') IS NOT NULL DROP TABLE dbo.Resultado;
CREATE TABLE dbo.Resultado (N INT IDENTITY, Caso VARCHAR(90), Esperado VARCHAR(90), Obtenido VARCHAR(90), Estado VARCHAR(6));
GO

CREATE OR ALTER PROCEDURE dbo.Afirmar @Caso VARCHAR(90), @Esperado SQL_VARIANT, @Obtenido SQL_VARIANT
AS
INSERT INTO dbo.Resultado (Caso, Esperado, Obtenido, Estado)
SELECT @Caso, CONVERT(VARCHAR(90), @Esperado), CONVERT(VARCHAR(90), @Obtenido),
       CASE WHEN CONVERT(VARCHAR(90), @Esperado) = CONVERT(VARCHAR(90), @Obtenido) THEN 'PASA' ELSE 'FALLA' END;
GO

DECLARE @filas INT, @carteras INT, @err INT, @res VARCHAR(12), @n INT;

-- ══════════════════════════════════════════════════════════════════════════
-- 1. Staging vacío. Es EXACTAMENTE la corrida del 18-sep 11:36 que vació la
--    tabla en producción: el Foreach no encontró ningún Excel.
-- ══════════════════════════════════════════════════════════════════════════
TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'Prueba'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH

SELECT @filas = COUNT(*) FROM dbo.UltimoEstadoTitular;
SELECT TOP (1) @res = Resultado FROM dbo.EtlEstadoTitular_Ejecucion ORDER BY EjecucionID DESC;
EXEC dbo.Afirmar '1. staging vacio -> lanza error 50038',        50038, @err;
EXEC dbo.Afirmar '1. staging vacio -> NO toca UltimoEstado',      2201,  @filas;
EXEC dbo.Afirmar '1. staging vacio -> auditoria ABORTADA',       'ABORTADA', @res;

-- ══════════════════════════════════════════════════════════════════════════
-- 2. Carga parcial: 4 carteras de 22, que es lo que quedó en el staging real
--    tras el OOM del 22-sep. Insertarlas habría dado una tabla que PARECE
--    válida y no lo es.
-- ══════════════════════════════════════════════════════════════════════════
TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, 'NUEVO', 900000, 2, '2026-09-23', '2026-09-23T11:00:00'
FROM dbo.UltimoEstadoTitular WHERE CarteraID BETWEEN 1 AND 4;

SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'Prueba'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH

SELECT @filas = COUNT(*) FROM dbo.UltimoEstadoTitular;
SELECT @n = COUNT(*) FROM dbo.UltimoEstadoTitular WHERE UltimoPerfil = 'NUEVO';
EXEC dbo.Afirmar '2. carga parcial 4/22 -> lanza error',          50038, @err;
EXEC dbo.Afirmar '2. carga parcial -> NO toca UltimoEstado',      2201,  @filas;
EXEC dbo.Afirmar '2. carga parcial -> no entro ni una fila',      0,     @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 3. Carga completa. Debe reemplazar las 22 carteras del staging y dejar
--    intacta la 99, que la escribió el Cargue de Reportes de Zentinela.
--    El procedimiento viejo, que hacía TRUNCATE, se la llevaba puesta.
-- ══════════════════════════════════════════════════════════════════════════
TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, 'NUEVO', 900000, 2, '2026-09-23', '2026-09-23T11:00:00'
FROM dbo.UltimoEstadoTitular WHERE CarteraID BETWEEN 1 AND 22;

-- Duplicado deliberado: mismo (Titular, Cartera), fecha de gestión igual y
-- FechaActualizacion posterior. Debe ganar el más reciente, no el azar.
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TOP (1) TitularID, CarteraID, AgenteID, 'GANADOR', 1, 3, '2026-09-23', '2026-09-23T18:00:00'
FROM dbo.Staging_UltimoEstadoTitular WHERE CarteraID = 1;

SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'Prueba'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH

SELECT @filas = COUNT(*) FROM dbo.UltimoEstadoTitular;
SELECT @n = COUNT(*) FROM dbo.UltimoEstadoTitular WHERE CarteraID = 99 AND UltimoPerfil = 'CARGUE MANUAL';
SELECT TOP (1) @res = Resultado FROM dbo.EtlEstadoTitular_Ejecucion ORDER BY EjecucionID DESC;
EXEC dbo.Afirmar '3. carga completa -> sin error',                0,     @err;
EXEC dbo.Afirmar '3. carga completa -> auditoria OK',            'OK',   @res;
EXEC dbo.Afirmar '3. carga completa -> 2200 nuevas + 1 ajena',    2201,  @filas;
EXEC dbo.Afirmar '3. REEMPLAZO ACOTADO: cartera 99 sobrevive',    1,     @n;

SELECT @n = COUNT(*) FROM dbo.UltimoEstadoTitular WHERE UltimoPerfil = 'NUEVO';
EXEC dbo.Afirmar '3. las 22 carteras quedaron actualizadas',      2199,  @n;
SELECT @n = COUNT(*) FROM dbo.UltimoEstadoTitular WHERE UltimoPerfil = 'GANADOR';
EXEC dbo.Afirmar '3. dedup determinista: gana el mas reciente',   1,     @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 4. Histórico: rotación cambió de 1 a 2, así que debe INSERTAR renglón nuevo
--    y conservar el anterior.
-- ══════════════════════════════════════════════════════════════════════════
DECLARE @histAntes INT = (SELECT COUNT(*) FROM dbo.HistoricoEstadoTitular);
SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Prueba'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH

SELECT @n = COUNT(*) FROM dbo.HistoricoEstadoTitular;
EXEC dbo.Afirmar '4. historico -> sin error',                     0,     @err;
-- 2200 previas + 2200 con rotacion nueva + 1 de la cartera 99, que no tenia
-- historico: lo que Zentinela carga a mano tambien debe quedar registrado.
EXEC dbo.Afirmar '4. historico -> inserto los cambiados + la 99', 4401,  @n;
SELECT @n = COUNT(*) FROM dbo.HistoricoEstadoTitular WHERE CarteraID = 99;
EXEC dbo.Afirmar '4. la cartera ajena llega al historico',        1,     @n;
SELECT @n = COUNT(*) FROM dbo.Staging_UltimoEstadoTitular;
EXEC dbo.Afirmar '4. staging liberado solo tras el exito',        0,     @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 5. Determinismo del "último histórico": dos filas del mismo (Titular,
--    Cartera) con FechaActualizacion IDÉNTICA. El JOIN contra MAX() del
--    procedimiento viejo devolvía las dos y podía duplicar; ROW_NUMBER no.
-- ══════════════════════════════════════════════════════════════════════════
INSERT INTO dbo.HistoricoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TOP (1) TitularID, CarteraID, AgenteID, 'EMPATE', 1, 2, '2026-09-23', '2026-09-23T11:00:00'
FROM dbo.HistoricoEstadoTitular WHERE CarteraID = 1 AND Rotacion = 2;

SET @histAntes = (SELECT COUNT(*) FROM dbo.HistoricoEstadoTitular);
-- La rotacion se copia tal cual de UltimoEstadoTitular: este escenario prueba
-- "nada cambio", asi que no debe entrar ni una fila nueva al historico.
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, 'NUEVO', 900000, Rotacion, '2026-09-24', '2026-09-24T11:00:00'
FROM dbo.UltimoEstadoTitular WHERE CarteraID BETWEEN 1 AND 22;

EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'Prueba';
EXEC dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Prueba';

SELECT @n = COUNT(*) FROM dbo.HistoricoEstadoTitular;
-- Rotación no cambió (sigue 2) => solo UPDATE, ninguna fila nueva.
EXEC dbo.Afirmar '5. rotacion igual -> no inserta duplicados',    @histAntes, @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 6. Histórico con UltimoEstadoTitular vacía. El procedimiento viejo purgaba
--    igual: borraba tres meses y no insertaba nada a cambio.
-- ══════════════════════════════════════════════════════════════════════════
SELECT * INTO dbo.Respaldo FROM dbo.UltimoEstadoTitular;
DELETE FROM dbo.UltimoEstadoTitular;
UPDATE dbo.HistoricoEstadoTitular SET FechaActualizacion = '2020-01-01';  -- todo purgable

SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Prueba'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH

SELECT @n = COUNT(*) FROM dbo.HistoricoEstadoTitular;
EXEC dbo.Afirmar '6. Ultimo vacia -> historico lanza error',      50039, @err;
EXEC dbo.Afirmar '6. Ultimo vacia -> NO purgo el historico',      @histAntes, @n;

INSERT INTO dbo.UltimoEstadoTitular SELECT * FROM dbo.Respaldo;
DROP TABLE dbo.Respaldo;

-- ══════════════════════════════════════════════════════════════════════════
-- 7. La vista de frescura tiene que poder contestar "¿cuándo se cargó bien?"
-- ══════════════════════════════════════════════════════════════════════════
SELECT @n = COUNT(*) FROM dbo.vw_EtlEstadoTitularEstado WHERE UltimaCargaOk IS NOT NULL;
EXEC dbo.Afirmar '7. vista de frescura responde',                 1, @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 8. Una corrida parcial que termina OK NO baja la vara (2026-09-24: un lote
--    OK de 2 carteras, y el ultimo dia del historico con ~4, dejaron pasar
--    despues un lote de 7 como si fuera una carga completa).
-- ══════════════════════════════════════════════════════════════════════════
INSERT INTO dbo.EtlEstadoTitular_Ejecucion (Paso, Origen, Forzada, Resultado, CarterasStaging, FilasInsertadas, FechaFin)
VALUES ('UltimoEstadoTitular', 'SSIS', 0, 'OK', 2, 150, SYSDATETIME());

TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, 'PARCIAL', 1, 7, '2026-09-25', '2026-09-25T10:00:00'
FROM dbo.UltimoEstadoTitular WHERE CarteraID BETWEEN 1 AND 7;

SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'SSIS'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH
SELECT @n = COUNT(*) FROM dbo.UltimoEstadoTitular WHERE UltimoPerfil = 'PARCIAL';
EXEC dbo.Afirmar '8. lote de 7 aborta aunque haya un OK de 2', 50038, @err;
EXEC dbo.Afirmar '8. y no publico ni una fila',                0,     @n;

-- ══════════════════════════════════════════════════════════════════════════
-- 9. El universo sale de Cartera: si se dan de baja 3 carteras, una carga de
--    las 19 que quedan es completa y pasa.
-- ══════════════════════════════════════════════════════════════════════════
UPDATE dbo.Cartera SET Activa = 0 WHERE ID IN (20, 21, 22);

TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;
INSERT INTO dbo.Staging_UltimoEstadoTitular
    (TitularID, CarteraID, AgenteID, UltimoPerfil, SaldoTotal, Rotacion, FechaUltimaGestion, FechaActualizacion)
SELECT TitularID, CarteraID, AgenteID, 'UNIVERSO19', 1, 8, '2026-09-25', '2026-09-25T11:00:00'
FROM dbo.UltimoEstadoTitular WHERE CarteraID BETWEEN 1 AND 19;

SET @err = 0;
BEGIN TRY EXEC dbo.sp_Insertar_UltimoEstadoTitular @Origen = 'SSIS'; END TRY
BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH
SELECT @n = COUNT(DISTINCT CarteraID) FROM dbo.UltimoEstadoTitular WHERE UltimoPerfil = 'UNIVERSO19';
EXEC dbo.Afirmar '9. universo de 19 activas: la carga pasa', 0,  @err;
EXEC dbo.Afirmar '9. y publica las 19 carteras',             19, @n;

UPDATE dbo.Cartera SET Activa = 1 WHERE ID IN (20, 21, 22);
GO

SELECT N, Caso, Esperado, Obtenido, Estado FROM dbo.Resultado ORDER BY N;
SELECT Estado, COUNT(*) AS casos FROM dbo.Resultado GROUP BY Estado;
GO

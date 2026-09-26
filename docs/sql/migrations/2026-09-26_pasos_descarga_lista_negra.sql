-- ============================================================================
-- ETL_UltimoEstadoTitular_Cargador hace TODO el ciclo, sin Power Automate
--
--   1. Descargar           descargar.py baja los 19 "Reporte Clientes" de ControlNext
--                          -> siempre sigue al paso 2: una cartera que no bajó conserva su
--                             CSV anterior y el resto se carga igual (queda en descargar.log)
--   2. Cargador            cargar.ps1 -Modo publicar (ya existía)
--                          -> bien: paso 3 · mal: paso 4
--   3. Lista Negra         arranca EjecutarETL_ListaNegra y termina el trabajo con ÉXITO
--   4. Lista Negra (falla) arranca EjecutarETL_ListaNegra y termina el trabajo con FALLA,
--                          para que el historial muestre que la carga no se publicó
--
--   La Lista Negra corre siempre, como en el flujo de Power Automate.
--
--   Requisito del paso 1: la credencial de ControlNext guardada EN EL SERVIDOR
--   (cargador\controlnext.credencial, con descargar.py --guardar-credencial). Sin ella el
--   paso 1 falla, se informa en el log y el paso 2 carga los CSV que haya.
--
--   Orden de migraciones: después de 2026-09-25_trabajos_cargador_estado_titular.sql
--   (que recrea el trabajo con un solo paso: si se reaplica, volver a aplicar esta).
--   Idempotente.
-- ============================================================================
USE msdb;
GO
SET NOCOUNT ON;

DECLARE @trabajo SYSNAME = N'ETL_UltimoEstadoTitular_Cargador';
DECLARE @base NVARCHAR(400) = N'E:\BASES\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular';
DECLARE @cargador NVARCHAR(400) = @base + N'\cargador';

IF NOT EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @trabajo)
    THROW 50042, N'No existe ETL_UltimoEstadoTitular_Cargador. Aplicar antes 2026-09-25_trabajos_cargador_estado_titular.sql.', 1;

-- Volver al estado base (solo el paso Cargador) para que reaplicar sea seguro.
DECLARE @id INT;
WHILE 1 = 1
BEGIN
    SELECT TOP (1) @id = s.step_id
    FROM dbo.sysjobsteps s JOIN dbo.sysjobs j ON j.job_id = s.job_id
    WHERE j.name = @trabajo AND s.step_name <> N'Cargador'
    ORDER BY s.step_id DESC;
    IF @@ROWCOUNT = 0 BREAK;
    EXEC dbo.sp_delete_jobstep @job_name = @trabajo, @step_id = @id;
END

-- 1. Descargar: se inserta ANTES de Cargador (que pasa a ser el paso 2).
DECLARE @descargar NVARCHAR(2000) =
      N'"' + @base + N'\Archivos .py\py.exe" "' + @cargador + N'\descargar.py"'
    + N' --salida "E:\BASES\SISTEMAS\Apps\ReporteClientes"';
EXEC dbo.sp_add_jobstep @job_name = @trabajo, @step_id = 1, @step_name = N'Descargar',
     @subsystem = N'CmdExec', @command = @descargar, @proxy_name = N'ProxySSIS',
     @cmdexec_success_code = 0, @on_success_action = 3, @on_fail_action = 3,
     @output_file_name = N'E:\BASES\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\cargador\logs\descargar.log',
     @flags = 0;

-- 2. Cargador: bien -> 3, mal -> 4.
EXEC dbo.sp_update_jobstep @job_name = @trabajo, @step_id = 2,
     @on_success_action = 3, @on_fail_action = 4, @on_fail_step_id = 4;

-- 3 y 4. Lista Negra. Si ya estaba corriendo, sp_start_job falla: no es motivo para
-- marcar la carga como fallida, así que se informa y se sigue.
DECLARE @lista NVARCHAR(1000) = N'
BEGIN TRY
    EXEC msdb.dbo.sp_start_job @job_name = N''EjecutarETL_ListaNegra'';
END TRY
BEGIN CATCH
    PRINT CONCAT(N''Lista Negra no arrancó: '', ERROR_MESSAGE());
END CATCH';

EXEC dbo.sp_add_jobstep @job_name = @trabajo, @step_id = 3, @step_name = N'Lista Negra',
     @subsystem = N'TSQL', @database_name = N'msdb', @command = @lista,
     @on_success_action = 1, @on_fail_action = 2;

DECLARE @lista_falla NVARCHAR(1200) = @lista + N'
THROW 50043, N''El cargador falló: la carga no se publicó (ver cargador\logs\publicar.log). La Lista Negra se lanzó igual.'', 1;';
EXEC dbo.sp_add_jobstep @job_name = @trabajo, @step_id = 4, @step_name = N'Lista Negra (cargador fallo)',
     @subsystem = N'TSQL', @database_name = N'msdb', @command = @lista_falla,
     @on_success_action = 2, @on_fail_action = 2;

EXEC dbo.sp_update_job @job_name = @trabajo, @start_step_id = 1,
     @description = N'ADR-039. Descarga los Reporte Clientes de ControlNext, carga UltimoEstadoTitular e HistoricoEstadoTitular y lanza la Lista Negra. Horario: L-V 7-17 cada 2 h, sábado 7-11.';
GO

SELECT s.step_id, s.step_name, s.subsystem, s.on_success_action, s.on_fail_action, s.on_fail_step_id
FROM msdb.dbo.sysjobsteps s JOIN msdb.dbo.sysjobs j ON j.job_id = s.job_id
WHERE j.name = N'ETL_UltimoEstadoTitular_Cargador'
ORDER BY s.step_id;
GO

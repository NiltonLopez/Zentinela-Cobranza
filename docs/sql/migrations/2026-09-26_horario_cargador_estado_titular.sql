-- ============================================================================
-- Horario del trabajo ETL_UltimoEstadoTitular_Cargador (ADR-039)
--
--   Lunes a viernes: 7, 9, 11, 13, 15 y 17 h
--   Sábado:          7, 9 y 11 h
--   Domingo:         nada
--
--   Reemplaza el disparo desde Power Automate. Aplicar SOLO cuando el trabajo ya
--   descargue los CSV (paso de descargar.py): si no, correría sobre los CSV que
--   dejó la última descarga de Power Automate.
--
--   Si cambia el horario, cambiar también dbo.fn_EtlEstadoTitular_CorridasPerdidas
--   (2026-09-26_alerta_por_horario_estado_titular.sql), o la alerta dará avisos falsos
--   o dejará de darlos.
--
--   El Agente no arranca un trabajo que sigue corriendo: si una corrida se alarga
--   más de 2 h, la siguiente se salta, no se superpone.
--
--   Idempotente: borra y recrea los dos horarios.
-- ============================================================================
USE msdb;
GO
SET NOCOUNT ON;

DECLARE @trabajo SYSNAME = N'ETL_UltimoEstadoTitular_Cargador';
IF NOT EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @trabajo)
    THROW 50041, N'No existe el trabajo ETL_UltimoEstadoTitular_Cargador. Aplicar antes 2026-09-25_trabajos_cargador_estado_titular.sql.', 1;

DECLARE @h SYSNAME;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name FROM dbo.sysschedules s
    WHERE s.name IN (N'ETL estado titular - lunes a viernes', N'ETL estado titular - sabado');
OPEN c; FETCH NEXT FROM c INTO @h;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC dbo.sp_delete_schedule @schedule_name = @h, @force_delete = 1;
    FETCH NEXT FROM c INTO @h;
END
CLOSE c; DEALLOCATE c;

-- La hora de fin va a :59:59 y no a la hora exacta: con fin 11:00:00 el Agente NO corría
-- la de las 11:00 (comprobado 2026-09-26: tras la de las 9 saltó al sábado siguiente).
-- freq_type 8 = semanal; freq_interval = suma de días (lunes 2, martes 4, miércoles 8,
-- jueves 16, viernes 32, sábado 64); freq_subday_type 8 = cada N horas.
EXEC dbo.sp_add_schedule
     @schedule_name = N'ETL estado titular - lunes a viernes', @enabled = 1,
     @freq_type = 8, @freq_interval = 62, @freq_recurrence_factor = 1,
     @freq_subday_type = 8, @freq_subday_interval = 2,
     @active_start_time = 070000, @active_end_time = 175959;
EXEC dbo.sp_attach_schedule @job_name = @trabajo, @schedule_name = N'ETL estado titular - lunes a viernes';

EXEC dbo.sp_add_schedule
     @schedule_name = N'ETL estado titular - sabado', @enabled = 1,
     @freq_type = 8, @freq_interval = 64, @freq_recurrence_factor = 1,
     @freq_subday_type = 8, @freq_subday_interval = 2,
     @active_start_time = 070000, @active_end_time = 115959;
EXEC dbo.sp_attach_schedule @job_name = @trabajo, @schedule_name = N'ETL estado titular - sabado';
GO

SELECT s.name AS horario, s.enabled, s.freq_interval AS dias, s.active_start_time AS desde,
       s.active_end_time AS hasta, js.next_run_date, js.next_run_time
FROM msdb.dbo.sysschedules s
JOIN msdb.dbo.sysjobschedules js ON js.schedule_id = s.schedule_id
JOIN msdb.dbo.sysjobs j ON j.job_id = js.job_id
WHERE j.name = N'ETL_UltimoEstadoTitular_Cargador';
GO

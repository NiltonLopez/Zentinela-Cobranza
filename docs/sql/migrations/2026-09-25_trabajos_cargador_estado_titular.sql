-- ============================================================================
-- Trabajos del Agente para el cargador de UltimoEstadoTitular (ADR-039)
--
--   ETL_UltimoEstadoTitular_Cargador            modo publicar: el que dispara Power Automate
--   ETL_UltimoEstadoTitular_Cargador_Comparar   modo comparar: no escribe nada, para verificar
--
-- Un solo paso CmdExec cada uno, ejecutado como ProxySSIS: la misma cuenta que hoy
-- corre el paquete y ya lee E:\BASES. La conexion a la base es integrada con esa
-- cuenta, asi que la contrasena de sa no aparece en ningun paso.
--
-- La salida de cada corrida queda en cargador\logs\<modo>.log (se sobrescribe), que
-- se puede leer desde \\10.0.0.89\bases\... sin entrar al servidor.
--
-- El trabajo viejo (Ejecutar_ETL_UltimoEstadoTitular) NO se toca: es el plan B.
--
-- Idempotente: si los trabajos ya existen se borran y se crean de nuevo (se pierde su
-- historial, no el del trabajo viejo).
-- ============================================================================
USE msdb;
GO
SET NOCOUNT ON;

DECLARE @base NVARCHAR(400) = N'E:\BASES\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular';
DECLARE @cargador NVARCHAR(400) = @base + N'\cargador';
DECLARE @py NVARCHAR(400) = @base + N'\Archivos .py';

-- Mismos argumentos para los dos modos; cambia -Modo y la carpeta de trabajo.
-- py.exe es el mismo lanzador que usa hoy la "Tarea Ejecutar proceso" del paquete.
DECLARE @comun NVARCHAR(2000) =
      N' -Entrada "E:\BASES\SISTEMAS\Apps\ReporteClientes"'
    + N' -Python "' + @py + N'\py.exe"'
    + N' -Conversor "' + @py + N'"'
    + N' -Libs "E:\BASES\SISTEMAS\Apps\ETL BlackList\libs"';

DECLARE @trabajos TABLE (Nombre SYSNAME, Modo VARCHAR(10), Descripcion NVARCHAR(512));
INSERT @trabajos VALUES
    (N'ETL_UltimoEstadoTitular_Cargador', 'publicar',
     N'ADR-039. Carga UltimoEstadoTitular e HistoricoEstadoTitular desde los CSV de ReporteClientes, sin SSIS. Lo dispara Power Automate.'),
    (N'ETL_UltimoEstadoTitular_Cargador_Comparar', 'comparar',
     N'ADR-039. Compara lo que cargaria el cargador contra UltimoEstadoTitular. No escribe nada.');

DECLARE @nombre SYSNAME, @modo VARCHAR(10), @desc NVARCHAR(512), @cmd NVARCHAR(4000), @log NVARCHAR(400);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Nombre, Modo, Descripcion FROM @trabajos;
OPEN c;
FETCH NEXT FROM c INTO @nombre, @modo, @desc;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @nombre)
        EXEC dbo.sp_delete_job @job_name = @nombre, @delete_unused_schedule = 1;

    SET @cmd = N'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'
             + @cargador + N'\cargar.ps1" -Modo ' + @modo + @comun
             + N' -Trabajo "' + @cargador + N'\_trabajo\' + @modo + N'"';
    SET @log = @cargador + N'\logs\' + @modo + N'.log';

    EXEC dbo.sp_add_job @job_name = @nombre, @enabled = 1, @description = @desc,
         @owner_login_name = N'sa', @notify_level_eventlog = 2;
    EXEC dbo.sp_add_jobstep @job_name = @nombre, @step_id = 1, @step_name = N'Cargador',
         @subsystem = N'CmdExec', @command = @cmd, @proxy_name = N'ProxySSIS',
         @cmdexec_success_code = 0, @on_success_action = 1, @on_fail_action = 2,
         @output_file_name = @log, @flags = 0;
    EXEC dbo.sp_add_jobserver @job_name = @nombre, @server_name = N'(local)';

    PRINT CONCAT(N'Trabajo creado: ', @nombre);
    FETCH NEXT FROM c INTO @nombre, @modo, @desc;
END
CLOSE c; DEALLOCATE c;

-- Un error en sp_add_jobstep no corta el lote: sin esta comprobacion el trabajo
-- quedaba creado, sin pasos, y el script decia "Trabajo creado" igual.
IF (SELECT COUNT(*) FROM dbo.sysjobs j
    WHERE j.name LIKE N'ETL_UltimoEstadoTitular_Cargador%'
      AND EXISTS (SELECT 1 FROM dbo.sysjobsteps s WHERE s.job_id = j.job_id AND s.subsystem = N'CmdExec')) <> 2
    THROW 50040, N'Los trabajos del cargador no quedaron completos (falta algun paso). Revisar los mensajes de arriba.', 1;
GO

SELECT j.name AS trabajo, s.subsystem, p.name AS proxy, s.output_file_name AS log
FROM dbo.sysjobs j
JOIN dbo.sysjobsteps s ON s.job_id = j.job_id
LEFT JOIN dbo.sysproxies p ON p.proxy_id = s.proxy_id
WHERE j.name LIKE N'ETL_UltimoEstadoTitular_Cargador%';
GO

-- ============================================================================
-- Rediseño del ETL de estado de titulares — DBCallCenter (ADR-038)
--
--   Reemplaza los dos procedimientos que el paquete SSIS ETL_UltimoEstadoTitular
--   invoca en su paso "Tarea Ejecutar SQL -> Insertar en tablas principales":
--
--     sp_Insertar_UltimoEstadoTitular      destruía la tabla antes de saber si tenía datos
--     sp_Insertar_HistoricoEstadoTitular   purgaba y recorría el histórico sin validar nada
--
--   Y agrega:
--     EtlEstadoTitular_Ejecucion   auditoría de cada corrida: qué trajo, qué hizo, qué abortó
--     vw_EtlEstadoTitularEstado    frescura en una sola fila, para que Zentinela la muestre
--
--   NO toca el paquete .dtsx: los nombres de los SP no cambian y todos los
--   parámetros nuevos tienen valor por defecto, así que la llamada sin argumentos
--   que hace SSIS sigue siendo válida.
--
--   NO cambia el esquema de UltimoEstadoTitular ni de HistoricoEstadoTitular: los
--   servicios de Zentinela que las leen no se enteran.
--
--   Idempotente. Reversible con el script _ROLLBACK de la misma fecha.
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>   (o abrirlo en SSMS).
--   Fecha: 2026-09-23.
-- ============================================================================

USE DBCallCenter;
GO

-- ----------------------------------------------------------------------------
-- 1. Auditoría de corridas
--
--    Sin esto no hay forma de contestar "¿cuándo se cargó bien por última vez?"
--    salvo mirando SSISDB, que reporta éxito aunque la corrida no haya hecho
--    nada. También es la referencia contra la que se comparan las compuertas:
--    cuántas carteras y cuántas filas trajo la última corrida buena.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.EtlEstadoTitular_Ejecucion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EtlEstadoTitular_Ejecucion
    (
        EjecucionID        BIGINT        IDENTITY(1,1) NOT NULL,
        Paso               VARCHAR(30)   NOT NULL,   -- UltimoEstadoTitular | HistoricoEstadoTitular
        Origen             VARCHAR(30)   NOT NULL,   -- SSIS | Manual
        Forzada            BIT           NOT NULL,   -- 1 = se saltaron las compuertas a propósito
        FechaInicio        DATETIME2(3)  NOT NULL CONSTRAINT DF_EtlEstadoTitular_Inicio DEFAULT (SYSDATETIME()),
        FechaFin           DATETIME2(3)  NULL,
        Resultado          VARCHAR(12)   NOT NULL CONSTRAINT DF_EtlEstadoTitular_Resultado DEFAULT ('EN CURSO'),
        Motivo             NVARCHAR(600) NULL,       -- por qué abortó, o el error

        FilasStaging       INT           NULL,
        CarterasStaging    INT           NULL,
        FilasAntes         INT           NULL,
        FilasBorradas      INT           NULL,
        FilasInsertadas    INT           NULL,
        FilasDespues       INT           NULL,
        FilasActualizadas  INT           NULL,       -- solo el paso de histórico
        FilasPurgadas      INT           NULL,       -- solo el paso de histórico

        CONSTRAINT PK_EtlEstadoTitular_Ejecucion PRIMARY KEY CLUSTERED (EjecucionID),
        CONSTRAINT CK_EtlEstadoTitular_Resultado
            CHECK (Resultado IN ('EN CURSO', 'OK', 'ABORTADA', 'ERROR'))
    );

    -- La búsqueda caliente es "última corrida OK de este paso".
    CREATE NONCLUSTERED INDEX IX_EtlEstadoTitular_Paso_Resultado
        ON dbo.EtlEstadoTitular_Ejecucion (Paso, Resultado, EjecucionID DESC)
        INCLUDE (FechaFin, CarterasStaging, FilasInsertadas);
END
GO

-- ----------------------------------------------------------------------------
-- 2. sp_Insertar_UltimoEstadoTitular
--
--    Qué hacía antes, y por qué se perdieron los datos el 18 de septiembre:
--
--        TRUNCATE TABLE UltimoEstadoTitular;   -- destruye primero
--        INSERT ... FROM Staging ...           -- y después ve qué había
--
--    Sin transacción y sin mirar el staging. La corrida de las 11:36 del 18-sep
--    no encontró ningún Excel (el contenedor Foreach duró 47 ms), llegó igual a
--    este procedimiento, truncó la tabla entera e insertó cero. El paquete
--    reportó ÉXITO. Cuántas filas se perdieron no quedó registrado en ninguna
--    parte: del orden de 2,3 millones, según el snapshot del 11-sep en el
--    histórico. Esa ceguera es una de las razones de la tabla de auditoría.
--
--    Tres cambios, en orden de importancia:
--
--    a) REEMPLAZO ACOTADO. Ya no se borra la tabla entera: se borran y se
--       reinsertan únicamente las carteras que vienen en el staging. Eso hace el
--       desastre estructuralmente imposible — staging vacío son cero carteras
--       que reemplazar, o sea cero filas tocadas — sin depender de que ningún
--       umbral esté bien calibrado. Efecto lateral deseable: deja de pisar lo
--       que el Cargue de Reportes de Zentinela haya escrito para carteras que el
--       Excel de esa noche no trae.
--
--    b) COMPUERTAS. Una corrida sin datos suficientes ABORTA con error, para que
--       SSIS marque el paso en rojo en vez de reportar éxito. Un fallo ruidoso
--       es infinitamente mejor que un éxito falso.
--
--    c) TRANSACCIÓN. El borrado y la inserción son un solo acto: nadie puede
--       leer la tabla a medio reemplazar.
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_Insertar_UltimoEstadoTitular
    -- Carteras mínimas que debe traer el staging. NULL = deducirlo de la última
    -- corrida buena, y si todavía no hay historial, del último día del histórico.
    @MinimoCarteras         INT         = NULL,
    -- Cuánto puede encoger el universo de carteras sin que se considere
    -- sospechoso. 10 % tolera que se retire una cartera de 22; no tolera que
    -- lleguen 4.
    @ToleranciaCarterasPct  INT         = 10,
    -- Cuánto puede caer el volumen de filas frente a la última corrida buena.
    @ToleranciaFilasPct     INT         = 30,
    -- 1 = ejecutar aunque las compuertas se quejen. Queda registrado como forzada.
    @Forzar                 BIT         = 0,
    @Origen                 VARCHAR(30) = 'SSIS'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EjecucionID     BIGINT,
            @Motivo          NVARCHAR(600) = NULL,
            @FilasStaging    INT,
            @CarterasStaging INT,
            @FilasAntes      INT,
            @FilasBorradas   INT = 0,
            @FilasInsertadas INT = 0,
            @CarterasRef     INT = NULL,
            @FilasRef        INT = NULL,
            @MinExigido      INT;

    SELECT @FilasStaging    = COUNT(*),
           @CarterasStaging = COUNT(DISTINCT CarteraID)
    FROM dbo.Staging_UltimoEstadoTitular;

    SELECT @FilasAntes = COUNT(*) FROM dbo.UltimoEstadoTitular;

    INSERT INTO dbo.EtlEstadoTitular_Ejecucion
        (Paso, Origen, Forzada, FilasStaging, CarterasStaging, FilasAntes)
    VALUES
        ('UltimoEstadoTitular', @Origen, @Forzar, @FilasStaging, @CarterasStaging, @FilasAntes);

    SET @EjecucionID = SCOPE_IDENTITY();

    BEGIN TRY
        -- ── Referencia: la última corrida que terminó bien ──────────────────
        SELECT TOP (1) @CarterasRef = CarterasStaging, @FilasRef = FilasInsertadas
        FROM dbo.EtlEstadoTitular_Ejecucion
        WHERE Paso = 'UltimoEstadoTitular' AND Resultado = 'OK'
        ORDER BY EjecucionID DESC;

        -- Primera corrida tras el despliegue: todavía no hay historial propio,
        -- así que la referencia sale del último día cargado en el histórico.
        IF @CarterasRef IS NULL
            SELECT @CarterasRef = COUNT(DISTINCT h.CarteraID)
            FROM dbo.HistoricoEstadoTitular h
            WHERE h.FechaActualizacion >= (
                      SELECT DATEADD(DAY, -1, MAX(h2.FechaActualizacion))
                      FROM dbo.HistoricoEstadoTitular h2);

        SET @MinExigido = ISNULL(
                              @MinimoCarteras,
                              CEILING(ISNULL(@CarterasRef, 1) * (100 - @ToleranciaCarterasPct) / 100.0));

        -- ── Compuertas ──────────────────────────────────────────────────────
        IF @FilasStaging = 0
            SET @Motivo = N'El staging está vacío: la carga de Excel no trajo ninguna fila. '
                        + N'No se modificó UltimoEstadoTitular.';

        ELSE IF @CarterasStaging < @MinExigido
            SET @Motivo = N'El staging trae ' + CAST(@CarterasStaging AS NVARCHAR(10))
                        + N' cartera(s) y se esperaban al menos ' + CAST(@MinExigido AS NVARCHAR(10))
                        + N'. Parece una carga incompleta. No se modificó UltimoEstadoTitular.';

        ELSE IF @FilasRef IS NOT NULL
                AND @FilasStaging < @FilasRef * (100 - @ToleranciaFilasPct) / 100.0
            SET @Motivo = N'El staging trae ' + CAST(@FilasStaging AS NVARCHAR(12))
                        + N' filas y la última corrida buena trajo ' + CAST(@FilasRef AS NVARCHAR(12))
                        + N'. La caída supera el ' + CAST(@ToleranciaFilasPct AS NVARCHAR(4))
                        + N' % tolerado. No se modificó UltimoEstadoTitular.';

        IF @Motivo IS NOT NULL AND @Forzar = 0
        BEGIN
            UPDATE dbo.EtlEstadoTitular_Ejecucion
            SET Resultado = 'ABORTADA', Motivo = @Motivo, FechaFin = SYSDATETIME()
            WHERE EjecucionID = @EjecucionID;

            -- THROW, no RETURN: SSIS tiene que ver el paso en rojo.
            THROW 50038, @Motivo, 1;
        END

        -- ── Snapshot deduplicado ────────────────────────────────────────────
        -- El desempate por FechaActualizacion es nuevo: FechaUltimaGestion es
        -- una fecha de negocio sin hora, empata muchísimo, y por sí sola dejaba
        -- el ganador librado al plan de ejecución.
        SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
               SaldoTotal, NumeroMarcado, Rotacion,
               FechaUltimaGestion, FechaPromesa, ValorPromesa,
               MotivoNoPago, Gestion, FechaActualizacion
        INTO #Nuevo
        FROM (
            SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
                   SaldoTotal, NumeroMarcado, Rotacion,
                   FechaUltimaGestion, FechaPromesa, ValorPromesa,
                   MotivoNoPago, Gestion, FechaActualizacion,
                   ROW_NUMBER() OVER (
                       PARTITION BY TitularID, CarteraID
                       ORDER BY FechaUltimaGestion DESC, FechaActualizacion DESC) AS rn
            FROM dbo.Staging_UltimoEstadoTitular
        ) s
        WHERE s.rn = 1;

        CREATE UNIQUE CLUSTERED INDEX IX_Nuevo ON #Nuevo (TitularID, CarteraID);

        SELECT DISTINCT CarteraID INTO #Carteras FROM #Nuevo;

        -- ── Reemplazo acotado y atómico ─────────────────────────────────────
        BEGIN TRANSACTION;

            DELETE u
            FROM dbo.UltimoEstadoTitular u
            WHERE EXISTS (SELECT 1 FROM #Carteras c WHERE c.CarteraID = u.CarteraID);

            SET @FilasBorradas = @@ROWCOUNT;

            INSERT INTO dbo.UltimoEstadoTitular
                (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
                 SaldoTotal, NumeroMarcado, Rotacion,
                 FechaUltimaGestion, FechaPromesa, ValorPromesa,
                 MotivoNoPago, Gestion, FechaActualizacion)
            SELECT TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
                   SaldoTotal, NumeroMarcado, Rotacion,
                   FechaUltimaGestion, FechaPromesa, ValorPromesa,
                   MotivoNoPago, Gestion, FechaActualizacion
            FROM #Nuevo;

            SET @FilasInsertadas = @@ROWCOUNT;

        COMMIT TRANSACTION;

        UPDATE dbo.EtlEstadoTitular_Ejecucion
        SET Resultado       = 'OK',
            Motivo          = CASE WHEN @Motivo IS NULL THEN NULL
                                   ELSE N'FORZADA pese a: ' + @Motivo END,
            FechaFin        = SYSDATETIME(),
            FilasBorradas   = @FilasBorradas,
            FilasInsertadas = @FilasInsertadas,
            FilasDespues    = (SELECT COUNT(*) FROM dbo.UltimoEstadoTitular)
        WHERE EjecucionID = @EjecucionID;

        DROP TABLE #Nuevo;
        DROP TABLE #Carteras;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        -- Una corrida abortada ya dejó su fila en ABORTADA; no la pisamos.
        UPDATE dbo.EtlEstadoTitular_Ejecucion
        SET Resultado = 'ERROR',
            Motivo    = LEFT(ERROR_MESSAGE(), 600),
            FechaFin  = SYSDATETIME()
        WHERE EjecucionID = @EjecucionID AND Resultado = 'EN CURSO';

        THROW;
    END CATCH
END
GO

-- ----------------------------------------------------------------------------
-- 3. sp_Insertar_HistoricoEstadoTitular
--
--    Cambios:
--
--    a) COMPUERTA. Si UltimoEstadoTitular está vacía, aborta antes de purgar.
--       Antes, con la tabla en cero, este procedimiento igual borraba tres meses
--       de histórico y no insertaba nada a cambio.
--
--    b) "ÚLTIMO REGISTRO" DETERMINISTA. El original lo resolvía con un JOIN
--       contra un GROUP BY ... MAX(FechaActualizacion). Cuando dos filas del
--       mismo (Titular, Cartera) comparten ese máximo — y pasa, porque la carga
--       escribe muchas filas con el mismo timestamp — el join devolvía las dos:
--       el UPDATE tocaba ambas y el LEFT JOIN del INSERT podía duplicar.
--       ROW_NUMBER garantiza exactamente una.
--
--    c) TRANSACCIÓN MÁS CORTA. La purga sale de la transacción grande y va en la
--       suya. Es mantenimiento, no parte del acto de cargar el día: que una
--       falle no tiene por qué deshacer la otra.
--
--    d) El TRUNCATE del staging sigue al final y sigue condicionado al éxito,
--       pero ahora queda auditado.
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_Insertar_HistoricoEstadoTitular
    @MesesRetencion INT         = 3,
    @Forzar         BIT         = 0,
    @Origen         VARCHAR(30) = 'SSIS'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EjecucionID       BIGINT,
            @Motivo            NVARCHAR(600) = NULL,
            @FilasUltimo       INT,
            @FilasAntes        INT,
            @FilasPurgadas     INT = 0,
            @FilasActualizadas INT = 0,
            @FilasInsertadas   INT = 0,
            @Corte             DATETIME;

    SELECT @FilasUltimo = COUNT(*) FROM dbo.UltimoEstadoTitular;

    SELECT @FilasAntes = SUM(p.rows)
    FROM sys.partitions p
    WHERE p.object_id = OBJECT_ID('dbo.HistoricoEstadoTitular') AND p.index_id IN (0, 1);

    INSERT INTO dbo.EtlEstadoTitular_Ejecucion
        (Paso, Origen, Forzada, FilasStaging, FilasAntes)
    VALUES
        ('HistoricoEstadoTitular', @Origen, @Forzar, @FilasUltimo, @FilasAntes);

    SET @EjecucionID = SCOPE_IDENTITY();

    BEGIN TRY
        IF @FilasUltimo = 0
            SET @Motivo = N'UltimoEstadoTitular está vacía: no hay nada que llevar al histórico, '
                        + N'y purgar ahora solo destruiría datos. No se modificó HistoricoEstadoTitular.';

        IF @Motivo IS NOT NULL AND @Forzar = 0
        BEGIN
            UPDATE dbo.EtlEstadoTitular_Ejecucion
            SET Resultado = 'ABORTADA', Motivo = @Motivo, FechaFin = SYSDATETIME()
            WHERE EjecucionID = @EjecucionID;

            THROW 50039, @Motivo, 1;
        END

        -- ── Purga de retención, en su propia transacción ────────────────────
        -- Ojo: se purga por FechaActualizacion, que NO es la columna de
        -- particionamiento (esa es FechaUltimaGestion). Por eso esto es un DELETE
        -- normal sobre el columnstore y no un switch de partición. Cambiarlo
        -- exigiría cambiar la semántica de la retención: decisión de negocio.
        SET @Corte = DATEADD(MONTH, -@MesesRetencion, GETDATE());

        IF EXISTS (SELECT 1 FROM dbo.HistoricoEstadoTitular WHERE FechaActualizacion < @Corte)
        BEGIN
            BEGIN TRANSACTION;
                DELETE FROM dbo.HistoricoEstadoTitular WHERE FechaActualizacion < @Corte;
                SET @FilasPurgadas = @@ROWCOUNT;
            COMMIT TRANSACTION;
        END

        -- ── Último registro histórico por (Titular, Cartera) ────────────────
        SELECT TitularID, CarteraID, UltimaRotacion, UltimaFecha
        INTO #UltimoHistorico
        FROM (
            SELECT TitularID, CarteraID,
                   Rotacion           AS UltimaRotacion,
                   FechaActualizacion AS UltimaFecha,
                   ROW_NUMBER() OVER (
                       PARTITION BY TitularID, CarteraID
                       ORDER BY FechaActualizacion DESC) AS rn
            FROM dbo.HistoricoEstadoTitular
        ) h
        WHERE h.rn = 1;

        CREATE UNIQUE CLUSTERED INDEX IX_UltimoHistorico ON #UltimoHistorico (TitularID, CarteraID);

        BEGIN TRANSACTION;

            -- 1. Misma rotación que el último histórico: no se inserta nada
            --    nuevo, solo se refresca la fecha y la gestión de esa fila.
            UPDATE H
            SET H.FechaActualizacion = U.FechaActualizacion,
                H.Gestion            = U.Gestion
            FROM dbo.HistoricoEstadoTitular H
            INNER JOIN #UltimoHistorico UH
                ON  H.TitularID          = UH.TitularID
                AND H.CarteraID          = UH.CarteraID
                AND H.FechaActualizacion = UH.UltimaFecha
            INNER JOIN dbo.UltimoEstadoTitular U
                ON  UH.TitularID = U.TitularID
                AND UH.CarteraID = U.CarteraID
                AND ISNULL(UH.UltimaRotacion, -1) = ISNULL(U.Rotacion, -1);

            SET @FilasActualizadas = @@ROWCOUNT;

            -- 2. Rotación distinta, o titular sin histórico: renglón nuevo, el
            --    anterior queda intacto.
            INSERT INTO dbo.HistoricoEstadoTitular
                (TitularID, CarteraID, AgenteID, UltimoPerfil, MejorPerfil,
                 SaldoTotal, NumeroMarcado, Rotacion,
                 FechaUltimaGestion, FechaPromesa, ValorPromesa,
                 MotivoNoPago, Gestion, FechaActualizacion)
            SELECT U.TitularID, U.CarteraID, U.AgenteID, U.UltimoPerfil, U.MejorPerfil,
                   U.SaldoTotal, U.NumeroMarcado, U.Rotacion,
                   U.FechaUltimaGestion, U.FechaPromesa, U.ValorPromesa,
                   U.MotivoNoPago, U.Gestion, U.FechaActualizacion
            FROM dbo.UltimoEstadoTitular U
            LEFT JOIN #UltimoHistorico UH
                ON  U.TitularID = UH.TitularID
                AND U.CarteraID = UH.CarteraID
            WHERE UH.TitularID IS NULL
               OR ISNULL(UH.UltimaRotacion, -1) <> ISNULL(U.Rotacion, -1);

            SET @FilasInsertadas = @@ROWCOUNT;

        COMMIT TRANSACTION;

        DROP TABLE #UltimoHistorico;

        -- El staging se libera solo cuando todo salió bien: si algo falló, los
        -- datos siguen ahí y la corrida se puede repetir sin volver a leer Excel.
        TRUNCATE TABLE dbo.Staging_UltimoEstadoTitular;

        UPDATE dbo.EtlEstadoTitular_Ejecucion
        SET Resultado         = 'OK',
            Motivo            = CASE WHEN @Motivo IS NULL THEN NULL
                                     ELSE N'FORZADA pese a: ' + @Motivo END,
            FechaFin          = SYSDATETIME(),
            FilasPurgadas     = @FilasPurgadas,
            FilasActualizadas = @FilasActualizadas,
            FilasInsertadas   = @FilasInsertadas,
            FilasDespues      = (SELECT SUM(p.rows) FROM sys.partitions p
                                 WHERE p.object_id = OBJECT_ID('dbo.HistoricoEstadoTitular')
                                   AND p.index_id IN (0, 1))
        WHERE EjecucionID = @EjecucionID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        IF OBJECT_ID('tempdb..#UltimoHistorico') IS NOT NULL DROP TABLE #UltimoHistorico;

        UPDATE dbo.EtlEstadoTitular_Ejecucion
        SET Resultado = 'ERROR',
            Motivo    = LEFT(ERROR_MESSAGE(), 600),
            FechaFin  = SYSDATETIME()
        WHERE EjecucionID = @EjecucionID AND Resultado = 'EN CURSO';

        THROW;
    END CATCH
END
GO

-- ----------------------------------------------------------------------------
-- 4. Frescura en una sola fila
--
--    Pensada para que Zentinela la muestre en el Comparador y el Calificador:
--    "datos al {UltimaCargaOk}". Sale toda de la tabla de auditoría, que es
--    chica: no cuenta filas de las tablas grandes en cada consulta.
-- ----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_EtlEstadoTitularEstado
AS
SELECT
    ok.FechaFin                                AS UltimaCargaOk,
    DATEDIFF(HOUR, ok.FechaFin, SYSDATETIME()) AS HorasDesdeCargaOk,
    ok.FilasInsertadas                         AS FilasUltimaCargaOk,
    ok.CarterasStaging                         AS CarterasUltimaCargaOk,
    ult.FechaInicio                            AS UltimoIntento,
    ult.Resultado                              AS ResultadoUltimoIntento,
    ult.Motivo                                 AS MotivoUltimoIntento
FROM
    (SELECT TOP (1) FechaFin, FilasInsertadas, CarterasStaging
     FROM dbo.EtlEstadoTitular_Ejecucion
     WHERE Paso = 'UltimoEstadoTitular' AND Resultado = 'OK'
     ORDER BY EjecucionID DESC) ok
FULL OUTER JOIN
    (SELECT TOP (1) FechaInicio, Resultado, Motivo
     FROM dbo.EtlEstadoTitular_Ejecucion
     WHERE Paso = 'UltimoEstadoTitular'
     ORDER BY EjecucionID DESC) ult ON 1 = 1;
GO

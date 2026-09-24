-- ============================================================================
-- Revertir UN cargue del módulo Cargue de Fuentes de Datos — DBCallCenter
-- (ADR-039)
--
--   Script MANUAL. No hay botón en la UI: revertir es excepcional y lo decide
--   una persona mirando los conteos. Se parametriza con @EjecucionID
--   (dbo.CargueFuenteEjecucion) y usa lo que el motor dejó en
--   dbo.CargueFuenteReversion dentro de la misma transacción del cargue.
--
--   Por defecto es un ENSAYO: hace todo, muestra los conteos y hace ROLLBACK.
--   Solo con @Confirmar = 1 hace COMMIT.
--
--   Qué deshace, por acción registrada:
--
--     'I' insertada    Se BORRA la fila del destino. Se ubica por TitularID +
--                      hash del contenido (recalculado con la MISMA expresión
--                      que GeneradorSqlCargue.HashContenido) + fecha de sello
--                      = SelloFechaConsulta de la ejecución.
--     'U' refrescada   Se le DEVUELVE la fecha que tenía (FechaAnterior). Su
--                      contenido no cambió: el cargue solo le movió la fecha.
--     'D' vacía        Se REINSERTA como fila vacía: TitularID + flag 0 +
--         purgada      fecha original. Limitación honesta: de la fila purgada
--                      solo se guardó la fecha (el hash de 'D' es 0x00), así
--                      que las columnas que NO entran al flag y que pudo tener
--                      (Departamento, Municipio, Regimen, TipoAfiliado,
--                      FechaNacimiento, UltimaFechaConsulta según la fuente)
--                      vuelven en NULL. Para SIMIT la reconstrucción es exacta
--                      (su única columna de dato es Placa, NULL en una vacía).
--                      Con @ReponerVacias = 0 no se reinsertan.
--
--   Qué NO deshace, y por qué:
--     - Titulares creados en dbo.Titular, nombres reparados en Titular.Nombre y
--       relaciones nuevas en dbo.Titular_Cartera. Son reparaciones ADITIVAS que
--       otras fuentes, cargues y gestiones pueden estar usando desde entonces;
--       borrarlas rompería FKs o devolvería nombres con mojibake. Además la
--       tabla de reversión no las registra: el motor solo guarda lo que tocó
--       en la tabla de la fuente.
--     - La caché del Calificador (en memoria de la API). Tras un COMMIT
--       reciclar el AppPool o esperar el corte diario de la 1 p. m.; si no, el
--       Calificador puede mostrar hasta 24 h las cifras con el cargue.
--
--   Guardas (THROW, no se toca nada):
--     - la ejecución no existe, no está en 'OK', ya fue revertida, o su fuente
--       no está en la lista blanca de abajo;
--     - hay un cargue EN CURSO de la misma fuente;
--     - hay ejecuciones POSTERIORES 'OK' de la misma fuente (salvo
--       @PermitirFueraDeOrden = 1). Revertir fuera de orden puede ser
--       incorrecto: una ejecución posterior pudo refrescar filas que esta
--       insertó (se borrarían aunque la posterior las "confirmó"), y sus 'U'
--       guardaron como FechaAnterior el sello de esta. Lo correcto es revertir
--       de la más nueva a la más vieja;
--     - la definición de la tabla destino ya no coincide con la lista blanca
--       (una columna renombrada cambiaría el hash y no se ubicaría nada);
--     - los conteos de lo revertido no cuadran con lo registrado (salvo
--       @PermitirDescuadre = 1): alguien tocó la tabla después del cargue.
--
--   Cómo queda registrado: CargueFuenteEjecucion.Estado tiene un CHECK
--   ('EN CURSO','OK','ERROR','CANCELADA') y este script NO cambia el esquema.
--   Estado se deja en 'OK' (el cargue sí terminó bien) y el hecho se escribe al
--   principio de la columna Error: "REVERTIDA <fecha> por <login> (...)". Esa
--   marca es además la guarda contra revertir dos veces. La lista "últimas 48 h"
--   de la UI la sigue mostrando como Completado (solo mira Estado). Las filas de
--   CargueFuenteReversion se conservan como evidencia.
--
--   Rendimiento y bloqueo: toma un bloqueo exclusivo de la tabla destino
--   (TABLOCKX) durante la transacción para que ningún cargue ni ETL la toque a
--   mitad. El Calificador y el Creador esperan mientras tanto: correrlo fuera
--   de horario si el cargue fue grande.
--
--   Cubre las 6 fuentes de la Fase 1. Para una fuente nueva: agregar su tabla
--   a @Fuentes y sus columnas de dato a @Columnas EN EL MISMO ORDEN que
--   ColumnasDato en CatalogoCargueFuentes (el orden entra al hash). Una fuente
--   con sello datetime (Fase 2) necesita además cambiar @Sello a datetime2.
--
--   SQL dinámico SEGURO: tablas y columnas salen solo de la lista blanca, pasan
--   por QUOTENAME y se verifican contra sys.columns; los valores viajan como
--   parámetros de sp_executesql.
--
--   Uso: fijar @EjecucionID, correr (ensayo), leer los conteos, y solo entonces
--   repetir con @Confirmar = 1.
--   sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i revertir_cargue_fuente.sql
--   Fecha: 2026-09-24.
-- ============================================================================

USE DBCallCenter;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── Parámetros ───────────────────────────────────────────────────────────────
DECLARE @EjecucionID          bigint = NULL;   -- ← la ejecución a revertir
DECLARE @Confirmar            bit    = 0;      -- 0 = ensayo con ROLLBACK | 1 = COMMIT
DECLARE @ReponerVacias        bit    = 1;      -- reinsertar las vacías purgadas ('D')
DECLARE @PermitirFueraDeOrden bit    = 0;      -- revertir aunque haya ejecuciones posteriores OK
DECLARE @PermitirDescuadre    bit    = 0;      -- confirmar aunque los conteos no cuadren

-- ── Lista blanca: definición de las fuentes de la Fase 1 ────────────────────
-- Copia de CatalogoCargueFuentes (Application/Constants). El ORDEN de @Columnas
-- es el de ColumnasDato: HashContenido concatena en ese orden.
DECLARE @Fuentes TABLE (
    Fuente       varchar(30) NOT NULL PRIMARY KEY,
    Tabla        sysname     NOT NULL,
    ColumnaFlag  sysname     NULL,      -- NULL = la fuente no tiene flag ni vacías (DatosClaro)
    ColumnaFecha sysname     NOT NULL,
    TitularTexto bit         NOT NULL   -- 1 = TitularID varchar(30) en el destino (NuevaEps)
);
INSERT INTO @Fuentes (Fuente, Tabla, ColumnaFlag, ColumnaFecha, TitularTexto) VALUES
    ('Sura',       'Sura',       'EstadoEnSura',      'FechaConsulta',  0),
    ('Cajacopi',   'Cajacopi',   'EstadoEnCajacopi',  'FechaConsulta',  0),
    ('MutualSer',  'MutualSer',  'EstadoEnMutualSer', 'FechaConsulta',  0),
    ('NuevaEps',   'NuevaEps',   'EstadoEnNuevaEps',  'FechaConsulta',  1),
    ('SIMIT',      'SIMIT',      'EstadoEnSIMIT',     'FechaConsulta',  0),
    ('DatosClaro', 'DatosClaro', NULL,                'FechaInsercion', 0);

DECLARE @Columnas TABLE (Fuente varchar(30) NOT NULL, Orden int NOT NULL, Columna sysname NOT NULL,
                         PRIMARY KEY (Fuente, Orden));
INSERT INTO @Columnas (Fuente, Orden, Columna) VALUES
    ('Sura', 1, 'NumeroCelular'), ('Sura', 2, 'Email'), ('Sura', 3, 'NIT'), ('Sura', 4, 'Empresa'),
    ('Sura', 5, 'TipoAfiliado'), ('Sura', 6, 'EstadoAfiliacion'), ('Sura', 7, 'UltimaFechaConsulta'),
    ('Cajacopi', 1, 'NumeroTelefonoCelular'), ('Cajacopi', 2, 'NumeroTelefonoFijo'), ('Cajacopi', 3, 'Email'),
    ('Cajacopi', 4, 'Departamento'), ('Cajacopi', 5, 'Municipio'), ('Cajacopi', 6, 'FechaNacimiento'),
    ('MutualSer', 1, 'NumeroTelefonoCelular'), ('MutualSer', 2, 'NumeroTelefonoCelular2'),
    ('MutualSer', 3, 'NumeroTelefonoFijo'), ('MutualSer', 4, 'Email'), ('MutualSer', 5, 'Regimen'),
    ('MutualSer', 6, 'TipoAfiliado'), ('MutualSer', 7, 'Departamento'), ('MutualSer', 8, 'Municipio'),
    ('NuevaEps', 1, 'NumeroTelefono'), ('NuevaEps', 2, 'EstadoAfiliacion'), ('NuevaEps', 3, 'TipoAfiliado'),
    ('NuevaEps', 4, 'FechaNacimiento'), ('NuevaEps', 5, 'Departamento'), ('NuevaEps', 6, 'Municipio'),
    ('SIMIT', 1, 'Placa'),
    ('DatosClaro', 1, 'NumeroTelefono'), ('DatosClaro', 2, 'Email');

-- ── Guardas sobre la ejecución ───────────────────────────────────────────────
DECLARE @Fuente varchar(30), @Estado varchar(12), @SelloDT datetime2(0), @ErrorPrevio nvarchar(1000),
        @InsertadasReg int, @RefrescadasReg int, @PurgadasReg int, @Msg nvarchar(2048);

IF @EjecucionID IS NULL
    THROW 50001, N'Fijar @EjecucionID antes de correr el script.', 1;

SELECT @Fuente = e.Fuente, @Estado = e.Estado, @SelloDT = e.SelloFechaConsulta, @ErrorPrevio = e.Error,
       @InsertadasReg = e.Insertadas, @RefrescadasReg = e.Refrescadas, @PurgadasReg = e.VaciasPurgadas
  FROM dbo.CargueFuenteEjecucion e
 WHERE e.EjecucionID = @EjecucionID;

IF @Fuente IS NULL
BEGIN
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' no existe en dbo.CargueFuenteEjecucion.');
    THROW 50002, @Msg, 1;
END
IF @Estado <> 'OK'
BEGIN
    -- ERROR / CANCELADA / EN CURSO: la transacción del cargue se revirtió (o sigue abierta); no hay nada que deshacer.
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' está en estado ''', @Estado, N''': solo se revierte una ejecución OK.');
    THROW 50003, @Msg, 1;
END
IF @ErrorPrevio LIKE N'REVERTIDA%'
BEGIN
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' ya fue revertida: ', @ErrorPrevio);
    THROW 50004, @Msg, 1;
END
IF @SelloDT IS NULL
    THROW 50005, N'La ejecución no tiene SelloFechaConsulta: no se pueden ubicar sus filas.', 1;

DECLARE @Tabla sysname, @ColFlag sysname, @ColFecha sysname, @TitularTexto bit;
SELECT @Tabla = f.Tabla, @ColFlag = f.ColumnaFlag, @ColFecha = f.ColumnaFecha, @TitularTexto = f.TitularTexto
  FROM @Fuentes f
 WHERE f.Fuente = @Fuente;
IF @Tabla IS NULL
BEGIN
    SET @Msg = CONCAT(N'La fuente ''', @Fuente, N''' no está en la lista blanca de este script (solo Fase 1).');
    THROW 50006, @Msg, 1;
END

IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion p WHERE p.Fuente = @Fuente AND p.Estado = 'EN CURSO')
BEGIN
    SET @Msg = CONCAT(N'Hay un cargue de ', @Fuente, N' EN CURSO. Esperar a que termine (o, si murió con un ',
                      N'reinicio, abrir la lista de cargues en la UI para que se cierre como Interrumpido).');
    THROW 50007, @Msg, 1;
END

DECLARE @Posteriores nvarchar(400) =
    (SELECT STRING_AGG(CONVERT(nvarchar(20), p.EjecucionID), N', ') WITHIN GROUP (ORDER BY p.EjecucionID)
       FROM dbo.CargueFuenteEjecucion p
      WHERE p.Fuente = @Fuente AND p.EjecucionID > @EjecucionID AND p.Estado = 'OK'
        AND ISNULL(p.Error, N'') NOT LIKE N'REVERTIDA%');
IF @Posteriores IS NOT NULL
BEGIN
    IF @PermitirFueraDeOrden = 0
    BEGIN
        SET @Msg = CONCAT(N'Hay ejecuciones posteriores OK de ', @Fuente, N' (', @Posteriores, N'). Revertirlas antes, ',
                          N'de la más nueva a la más vieja, o correr con @PermitirFueraDeOrden = 1 sabiendo el riesgo.');
        THROW 50008, @Msg, 1;
    END
    PRINT CONCAT(N'⚠ Revirtiendo FUERA DE ORDEN: hay ejecuciones posteriores OK de ', @Fuente, N' (', @Posteriores, N').');
END

-- La definición real de la tabla debe seguir siendo la de la lista blanca.
IF COL_LENGTH(N'dbo.' + @Tabla, N'TitularID') IS NULL
   OR COL_LENGTH(N'dbo.' + @Tabla, @ColFecha) IS NULL
   OR (@ColFlag IS NOT NULL AND COL_LENGTH(N'dbo.' + @Tabla, @ColFlag) IS NULL)
   OR EXISTS (SELECT 1 FROM @Columnas c WHERE c.Fuente = @Fuente AND COL_LENGTH(N'dbo.' + @Tabla, c.Columna) IS NULL)
BEGIN
    SET @Msg = CONCAT(N'dbo.', @Tabla, N' ya no tiene las columnas de la lista blanca: actualizar el script antes de revertir.');
    THROW 50009, @Msg, 1;
END

-- ── Lo registrado ────────────────────────────────────────────────────────────
DECLARE @RegI int, @RegU int, @RegD int;
SELECT @RegI = ISNULL(SUM(CASE WHEN r.Accion = 'I' THEN 1 ELSE 0 END), 0),
       @RegU = ISNULL(SUM(CASE WHEN r.Accion = 'U' THEN 1 ELSE 0 END), 0),
       @RegD = ISNULL(SUM(CASE WHEN r.Accion = 'D' THEN 1 ELSE 0 END), 0)
  FROM dbo.CargueFuenteReversion r
 WHERE r.EjecucionID = @EjecucionID;

PRINT CONCAT(N'Ejecución ', @EjecucionID, N' — ', @Fuente, N' — sello ', CONVERT(char(10), @SelloDT, 23));
PRINT CONCAT(N'  Registrado en CargueFuenteEjecucion: insertadas ', @InsertadasReg, N', refrescadas ', @RefrescadasReg,
             N', vacías purgadas ', @PurgadasReg);
PRINT CONCAT(N'  Filas en CargueFuenteReversion:      I ', @RegI, N', U ', @RegU, N', D ', @RegD);
-- U puede superar a Refrescadas: Refrescadas se cuenta del lado del archivo y una fila del
-- archivo refresca TODAS las filas idénticas del destino (duplicados legacy).

IF @RegI + @RegU + @RegD = 0
    THROW 50010, N'La ejecución no tiene filas en CargueFuenteReversion: no hay nada que revertir.', 1;
IF (@RegI <> ISNULL(@InsertadasReg, -1) OR @RegD <> ISNULL(@PurgadasReg, -1) OR @RegU < ISNULL(@RefrescadasReg, 0))
   AND @PermitirDescuadre = 0
    THROW 50011, N'CargueFuenteReversion no cuadra con los conteos de la ejecución. Revisar a mano (o @PermitirDescuadre = 1).', 1;

-- ── SQL dinámico desde la lista blanca ──────────────────────────────────────
DECLARE @T nvarchar(300) = QUOTENAME(N'dbo') + N'.' + QUOTENAME(@Tabla);
DECLARE @F nvarchar(300) = QUOTENAME(@ColFecha);

-- Hash del contenido: MISMA expresión que GeneradorSqlCargue.HashContenido
--   HASHBYTES('SHA2_256', CONCAT_WS(N'|', ISNULL(CONVERT(nvarchar(400), s.[Col]), N'~'), ...))
-- con una sola columna (SIMIT) va sin CONCAT_WS, igual que el generador.
DECLARE @H nvarchar(max), @NCols int;
SELECT @NCols = COUNT(*),
       @H = STRING_AGG(CONVERT(nvarchar(max), N'ISNULL(CONVERT(nvarchar(400), s.' + QUOTENAME(c.Columna) + N'), N''~'')'), N', ')
                WITHIN GROUP (ORDER BY c.Orden)
  FROM @Columnas c
 WHERE c.Fuente = @Fuente;
SET @H = N'HASHBYTES(''SHA2_256'', ' + CASE WHEN @NCols = 1 THEN @H ELSE N'CONCAT_WS(N''|'', ' + @H + N')' END + N')';

-- Titular de CargueFuenteReversion (varchar) contra el destino: la conversión va del lado de r
-- para que s.TitularID use su índice (IX_<Tabla>_TitularID_...).
DECLARE @MismoTitular nvarchar(200) = CASE WHEN @TitularTexto = 1
    THEN N's.[TitularID] = r.TitularID'
    ELSE N's.[TitularID] = CONVERT(bigint, r.TitularID)' END;
DECLARE @TitularDesdeR nvarchar(100) = CASE WHEN @TitularTexto = 1 THEN N'r.TitularID' ELSE N'CONVERT(bigint, r.TitularID)' END;

-- 'I': borrar, por (titular, hash), a lo sumo tantas filas como registró la ejecución.
-- DELETE sobre una CTE de una sola tabla (el ROW_NUMBER numera filas idénticas; el destino es heap).
DECLARE @SqlI nvarchar(max) = N'
;WITH d AS (
    SELECT CONVERT(varchar(30), s.[TitularID]) AS _T, ' + @H + N' AS _H,
           ROW_NUMBER() OVER (PARTITION BY s.[TitularID], ' + @H + N' ORDER BY (SELECT NULL)) AS _rn
      FROM ' + @T + N' AS s
     WHERE s.' + @F + N' = @Sello
       AND EXISTS (SELECT 1 FROM dbo.CargueFuenteReversion r
                    WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''I'' AND ' + @MismoTitular + N'))
DELETE FROM d
 WHERE d._rn <= (SELECT COUNT(*) FROM dbo.CargueFuenteReversion r
                  WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''I''
                    AND r.TitularID = d._T AND r.HashContenido = d._H);
SET @n = @@ROWCOUNT;';

-- 'U': devolver la fecha anterior. Las filas de un mismo (titular, hash) son idénticas (mismo
-- contenido, misma fecha de sello): se emparejan por número de fila y da igual cuál recibe cuál.
DECLARE @SqlU nvarchar(max) = N'
;WITH d AS (
    SELECT s.' + @F + N' AS _Fecha, CONVERT(varchar(30), s.[TitularID]) AS _T, ' + @H + N' AS _H,
           ROW_NUMBER() OVER (PARTITION BY s.[TitularID], ' + @H + N' ORDER BY (SELECT NULL)) AS _rn
      FROM ' + @T + N' AS s
     WHERE s.' + @F + N' = @Sello
       AND EXISTS (SELECT 1 FROM dbo.CargueFuenteReversion r
                    WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''U'' AND ' + @MismoTitular + N')),
u AS (
    SELECT r.TitularID, r.HashContenido, r.FechaAnterior,
           ROW_NUMBER() OVER (PARTITION BY r.TitularID, r.HashContenido ORDER BY r.FechaAnterior) AS _rn
      FROM dbo.CargueFuenteReversion r
     WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''U'')
UPDATE d SET d._Fecha = CONVERT(date, u.FechaAnterior)
  FROM d
  JOIN u ON u.TitularID = d._T AND u.HashContenido = d._H AND u._rn = d._rn;
SET @n = @@ROWCOUNT;';

-- 'D': reinsertar la vacía (TitularID + flag 0 + fecha original; el resto NULL, ver cabecera).
DECLARE @SqlD nvarchar(max) = CASE WHEN @ColFlag IS NULL THEN NULL ELSE N'
INSERT INTO ' + @T + N' ([TitularID], ' + QUOTENAME(@ColFlag) + N', ' + @F + N')
SELECT ' + @TitularDesdeR + N', 0, CONVERT(date, r.FechaAnterior)
  FROM dbo.CargueFuenteReversion r
 WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''D'';
SET @n = @@ROWCOUNT;' END;

DECLARE @Bloqueo nvarchar(max) = N'SELECT TOP (0) 1 FROM ' + @T + N' WITH (TABLOCKX, HOLDLOCK);';
DECLARE @Params nvarchar(200) = N'@EjecucionID bigint, @Sello date, @n int OUTPUT';
DECLARE @Sello date = CONVERT(date, @SelloDT);
DECLARE @n int, @Borradas int = 0, @Restauradas int = 0, @Repuestas int = 0;

-- ── Transacción ──────────────────────────────────────────────────────────────
BEGIN TRY
    BEGIN TRANSACTION;

    EXEC sys.sp_executesql @Bloqueo;

    EXEC sys.sp_executesql @SqlI, @Params, @EjecucionID = @EjecucionID, @Sello = @Sello, @n = @n OUTPUT;
    SET @Borradas = @n;

    EXEC sys.sp_executesql @SqlU, @Params, @EjecucionID = @EjecucionID, @Sello = @Sello, @n = @n OUTPUT;
    SET @Restauradas = @n;

    IF @SqlD IS NOT NULL AND @ReponerVacias = 1
    BEGIN
        EXEC sys.sp_executesql @SqlD, @Params, @EjecucionID = @EjecucionID, @Sello = @Sello, @n = @n OUTPUT;
        SET @Repuestas = @n;
    END

    PRINT CONCAT(N'  Borradas (I): ', @Borradas, N' de ', @RegI,
                 N' | Fecha devuelta (U): ', @Restauradas, N' de ', @RegU,
                 N' | Vacías repuestas (D): ', @Repuestas, N' de ', CASE WHEN @ReponerVacias = 1 THEN @RegD ELSE 0 END);

    -- Si no cuadra, alguien tocó la tabla después del cargue (otro cargue, un ETL, un UPDATE a mano).
    IF (@Borradas <> @RegI OR @Restauradas <> @RegU
        OR @Repuestas <> CASE WHEN @ReponerVacias = 1 AND @SqlD IS NOT NULL THEN @RegD ELSE 0 END)
       AND @PermitirDescuadre = 0
        THROW 50012, N'Lo revertido no cuadra con CargueFuenteReversion: se deshizo todo. Revisar qué tocó la tabla después del cargue.', 1;

    UPDATE dbo.CargueFuenteEjecucion
       SET Error = LEFT(CONCAT(N'REVERTIDA ', CONVERT(char(19), SYSDATETIME(), 120), N' por ', SUSER_SNAME(),
                               N' (revertir_cargue_fuente.sql): I ', @Borradas, N', U ', @Restauradas, N', D ', @Repuestas,
                               CASE WHEN @Posteriores IS NOT NULL THEN N', FUERA DE ORDEN' ELSE N'' END,
                               CASE WHEN @ErrorPrevio IS NOT NULL THEN N' | ' + @ErrorPrevio ELSE N'' END), 1000)
     WHERE EjecucionID = @EjecucionID AND Estado = 'OK';

    IF @Confirmar = 1
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'COMMIT: ejecución revertida. Reciclar el AppPool (o esperar la 1 p. m.) para limpiar la caché del Calificador.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'ENSAYO: se revirtió todo (ROLLBACK). Si los conteos son los esperados, repetir con @Confirmar = 1.';
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

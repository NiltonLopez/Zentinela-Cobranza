-- ============================================================================
-- Cargue de Fuentes de Datos — DBCallCenter (ADR-039)
--
--   Módulo de Administración que reemplaza el proceso manual por fuente:
--
--     <Fuente>Auxiliar                 staging que se llenaba a mano con el archivo
--     sp_Procesar<Fuente>Auxiliar      un SP por fuente que movía staging -> tabla final
--
--   por un motor genérico en la API: lee el archivo, lo vuelca a tablas #temp,
--   muestra un preview con los conteos y aplica todo en UNA transacción. Las
--   tablas <Fuente>Auxiliar y sus SP no se tocan aquí; quedan como estaban.
--
--   Tablas nuevas:
--     CargueFuenteEjecucion   auditoría de cada ejecución: quién, qué archivo,
--                             qué hizo y cómo terminó. Mismo patrón que
--                             EtlEstadoTitular_Ejecucion (ADR-038).
--     CargueFuenteReversion   qué filas tocó cada ejecución, para poder deshacerla.
--
--   Por qué hace falta la tabla de reversión (y no basta con la fecha):
--     - El sello FechaConsulta de las fuentes es DATE: dos cargues del mismo día
--       de la misma fuente comparten sello, así que "borrar lo de hoy" se lleva
--       también el cargue anterior.
--     - Las filas "refrescadas" (la misma información que ya existía, solo con
--       fecha nueva) no se distinguen de las insertadas mirando la tabla final.
--       Para deshacerlas hay que devolverles la fecha anterior, no borrarlas.
--     - Las filas vacías purgadas por el cargue desaparecen; sin guardar su
--       fecha no se pueden reponer.
--
--   Idempotente. No crea objetos en otras bases. Rollback al final (comentado).
--   Aplicar con: sqlcmd -f 65001 -i <este archivo>   (o abrirlo en SSMS).
--   Fecha: 2026-09-24.
-- ============================================================================

USE DBCallCenter;
GO

-- ----------------------------------------------------------------------------
-- 1. Auditoría de ejecuciones
--
--    Una fila por cargue. Se inserta 'EN CURSO' al empezar y se cierra con
--    OK / ERROR / CANCELADA al terminar, con los conteos que mostró el preview.
--    Una fila que se queda 'EN CURSO' indica que el proceso murió a mitad.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.CargueFuenteEjecucion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueFuenteEjecucion
    (
        EjecucionID        BIGINT           IDENTITY(1,1) NOT NULL,
        JobID              UNIQUEIDENTIFIER NOT NULL,     -- id del job en memoria de la API; enlaza la fila con el preview que el usuario confirmó
        Fuente             VARCHAR(30)      NOT NULL,     -- nombre de la fuente destino (p. ej. Sura, NuevaEps)
        Usuario            VARCHAR(100)     NOT NULL,     -- usuario de Zentinela que lanzó el cargue
        NombreArchivo      NVARCHAR(260)    NOT NULL,     -- nombre original del archivo subido
        HuellaArchivo      CHAR(64)         NOT NULL,     -- SHA-256 hex del archivo, detecta re-cargues del mismo archivo
        FechaInicio        DATETIME2(3)     NOT NULL CONSTRAINT DF_CargueFuenteEjecucion_Inicio DEFAULT (SYSDATETIME()),
        FechaFin           DATETIME2(3)     NULL,         -- NULL mientras está EN CURSO
        Estado             VARCHAR(12)      NOT NULL CONSTRAINT DF_CargueFuenteEjecucion_Estado DEFAULT ('EN CURSO'),
        SelloFechaConsulta DATETIME2(0)     NULL,         -- valor exacto usado como FechaConsulta/FechaInsercion en la fuente

        TotalLeidas        INT              NULL,         -- filas leídas del archivo
        Excluidas          INT              NULL,         -- filas excluidas (un solo motivo por fila): documento inválido, fila corrida, sin tel ni email, titular nuevo sin nombre
        DuplicadasArchivo  INT              NULL,         -- filas repetidas dentro del mismo archivo
        Candidatas         INT              NULL,         -- filas válidas que pasan a comparar contra la fuente
        Insertadas         INT              NULL,         -- filas nuevas en la tabla de la fuente
        Refrescadas        INT              NULL,         -- filas iguales a las existentes: solo se les actualizó la fecha
        DescartadasVacias  INT              NULL,         -- filas del archivo sin ningún dato útil, no se insertan
        VaciasPurgadas     INT              NULL,         -- filas vacías que ya estaban en la fuente y se eliminaron
        TitularesNuevos    INT              NULL,         -- titulares que no existían en dbo.Titular y se crearon
        NombresReparados   INT              NULL,         -- Titular.Nombre reemplazado porque el existente estaba vacío, con mojibake (Ã) o con ? (nunca se pisa un nombre sano)
        RelacionesNuevas   INT              NULL,         -- filas nuevas en Titular_Cartera (solo pares Lider_Cartera activos)
        Advertencias       INT              NULL,         -- avisos no bloqueantes mostrados en el preview
        Error              NVARCHAR(1000)   NULL,         -- mensaje de error cuando Estado = 'ERROR'

        CONSTRAINT PK_CargueFuenteEjecucion PRIMARY KEY CLUSTERED (EjecucionID),
        CONSTRAINT CK_CargueFuenteEjecucion_Estado
            CHECK (Estado IN ('EN CURSO', 'OK', 'ERROR', 'CANCELADA'))
    );
    -- Historial por fuente, lo más reciente primero (pantalla de ejecuciones).
    CREATE NONCLUSTERED INDEX IX_CargueFuenteEjecucion_Fuente
        ON dbo.CargueFuenteEjecucion (Fuente, EjecucionID DESC);
END
GO

-- ----------------------------------------------------------------------------
-- 2. Reversión
--
--    Una fila por titular tocado en cada ejecución. La llena el motor con
--    OUTPUT ... INTO dentro de la misma transacción del cargue.
--
--    ⚠ SIN FK, SIN CHECK y SIN triggers A PROPÓSITO: SQL Server prohíbe que el
--    destino de OUTPUT ... INTO participe en una FK (de cualquier lado), tenga
--    CHECK habilitados o triggers. Agregar cualquiera de ellos rompe el cargue
--    en runtime. La integridad (EjecucionID existente, Accion válida) la
--    garantiza el motor.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.CargueFuenteReversion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CargueFuenteReversion
    (
        EjecucionID   BIGINT        NOT NULL,   -- ejecución que tocó la fila (CargueFuenteEjecucion.EjecucionID)
        TitularID     VARCHAR(30)   NOT NULL,   -- documento del titular; VARCHAR porque NuevaEps lo guarda como texto
        HashContenido BINARY(32)    NOT NULL,   -- HASHBYTES('SHA2_256') de las columnas de dato, '~' como centinela de NULL; 0x00 para 'D'
        Accion        CHAR(1)       NOT NULL,   -- I = insertada | U = refrescada | D = vacía purgada
        FechaAnterior DATETIME2(0)  NULL        -- U: fecha que tenía antes del refresco | D: fecha de la fila purgada | I: NULL
    );
    -- Deshacer una ejecución lee todas sus filas por EjecucionID.
    CREATE CLUSTERED INDEX IX_CargueFuenteReversion
        ON dbo.CargueFuenteReversion (EjecucionID, TitularID);
END
GO

-- Verificación
SELECT name, create_date
  FROM sys.tables
 WHERE name IN ('CargueFuenteEjecucion', 'CargueFuenteReversion')
 ORDER BY name;
GO

-- ----------------------------------------------------------------------------
-- Rollback (NO ejecutar salvo para revertir esta migración; se pierde la
-- auditoría y la posibilidad de deshacer los cargues ya hechos):
--
-- DROP TABLE IF EXISTS dbo.CargueFuenteReversion;
-- DROP TABLE IF EXISTS dbo.CargueFuenteEjecucion;
-- ----------------------------------------------------------------------------

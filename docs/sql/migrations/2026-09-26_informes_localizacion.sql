-- ============================================================================
-- Informes de Localización — DBCallCenter (ADR-040)
--
--   Sub-módulo de Informes que mide la localización por líder y por cartera:
--   cobertura por fuente, frescura, rankings con foto y, en etapas siguientes,
--   sugerencias y envíos al departamento de localización.
--
--   Las cifras por titular (22 fuentes, ~1,9 M titulares) no se calculan en
--   cada consulta: la API arma un "corte" (precálculo) y lo guarda aquí. Se
--   recalcula tras cada cargue OK del Cargue de Fuentes, al primer acceso
--   después de las 13:00, al arrancar la API si no hay corte del día y con el
--   botón de Admin. Los informes leen el último corte OK.
--
--   Tablas nuevas:
--     LocalizacionCorte              un precálculo: cuándo, por qué, cómo terminó.
--     LocalizacionResumenFuente      por corte, par (líder, cartera) y fuente.
--     LocalizacionResumenFuenteLider por corte, líder y fuente, con titulares
--                                    DISTINTOS (LiderID 0 = todos los pares).
--     LocalizacionResumenPar         métricas que cruzan fuentes, por par.
--     LocalizacionResumenLider       lo mismo por líder, titulares distintos,
--                                    con y sin relaciones externas.
--     LocalizacionEnvio              lotes enviados a localización (etapa 3).
--     LocalizacionEnvioDetalle       titulares de cada lote con la foto de su
--                                    estado al enviarlo (etapa 3).
--
--   Las tablas de envíos se crean ya para que haya un solo script; la API las
--   empieza a usar en la etapa 3.
--
--   Retención: la API borra los cortes de más de 26 semanas (las filas de
--   resumen se van en cascada). Tamaño esperado por corte: ~1.200 filas en
--   ResumenFuente, ~600 en ResumenFuenteLider, ~60 en Par y Lider.
--
--   Excluidas de las migraciones de EF (patrón de CargueFuenteEjecucion): la
--   API las usa con ADO.NET. Mientras no existan, los endpoints de
--   /api/informes/localizacion responden 503 con el nombre de este archivo.
--
--   Idempotente. No crea objetos en otras bases. Rollback al final (comentado).
--   Aplicar con: sqlcmd -b -f 65001 -i <este archivo>   (o abrirlo en SSMS).
--   Fecha: 2026-09-26.
-- ============================================================================

USE DBCallCenter;
GO

-- sqlcmd arranca con QUOTED_IDENTIFIER OFF; las opciones que exigen columnas calculadas e
-- índices quedan fijas para todo el script.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ----------------------------------------------------------------------------
-- 1. Cortes
--
--    Una fila por precálculo. Se inserta 'EN CURSO' al empezar y se cierra con
--    OK / ERROR. Los informes leen solo cortes OK. Una fila que se queda
--    'EN CURSO' indica que el proceso murió a mitad (la API la marca ERROR en
--    la siguiente corrida).
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionCorte', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionCorte
    (
        CorteID        INT            IDENTITY(1,1) NOT NULL,
        FechaCorte     DATETIME2(0)   NOT NULL CONSTRAINT DF_LocalizacionCorte_Fecha DEFAULT (SYSDATETIME()),  -- inicio del cálculo: "localización al ..."
        Motivo         VARCHAR(20)    NOT NULL,     -- ARRANQUE | CORTE_13H | CARGUE_FUENTE | MANUAL
        Usuario        VARCHAR(100)   NULL,         -- quién lo pidió (MANUAL); NULL si lo disparó el sistema
        Estado         VARCHAR(12)    NOT NULL CONSTRAINT DF_LocalizacionCorte_Estado DEFAULT ('EN CURSO'),
        DuracionMs     INT            NULL,         -- duración del batch, NULL mientras está EN CURSO
        Pares          INT            NULL,         -- pares (líder, cartera) activos con titulares
        Titulares      INT            NULL,         -- titulares distintos en esos pares
        DatosGestionAl DATETIME2(0)   NULL,         -- vw_EtlEstadoTitularEstado.UltimaCargaOk al calcular: "gestión al ..."
        Error          NVARCHAR(1000) NULL,         -- mensaje cuando Estado = 'ERROR'

        CONSTRAINT PK_LocalizacionCorte PRIMARY KEY CLUSTERED (CorteID),
        CONSTRAINT CK_LocalizacionCorte_Estado CHECK (Estado IN ('EN CURSO', 'OK', 'ERROR')),
        CONSTRAINT CK_LocalizacionCorte_Motivo CHECK (Motivo IN ('ARRANQUE', 'CORTE_13H', 'CARGUE_FUENTE', 'MANUAL'))
    );
    -- Último corte OK (lo que leen los informes) y corte anterior para las variaciones.
    CREATE NONCLUSTERED INDEX IX_LocalizacionCorte_Estado
        ON dbo.LocalizacionCorte (Estado, CorteID DESC) INCLUDE (FechaCorte);
END
GO

-- ----------------------------------------------------------------------------
-- 2. Resumen por par y fuente
--
--    Total        filas de Titular_Cartera del par (como el Calificador).
--    Buscados     titulares con al menos una fila en la fuente.
--    Encontrados  la ÚLTIMA fila del titular cumple el predicado "encontrado"
--                 (CatalogoFuentesLocalizacion, el mismo del Calificador).
--    No encontrados = Buscados - Encontrados; Falta por buscar = Total - Buscados.
--    ViejosN      buscados cuya última consulta tiene más de N días (o no
--                 tiene fecha). Tramos de frescura: <=90 = Buscados - Viejos90,
--                 91-180 = Viejos90 - Viejos180, 181-365 = Viejos180 - Viejos365,
--                 >365 = Viejos365.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionResumenFuente', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionResumenFuente
    (
        CorteID           INT         NOT NULL,
        LiderID           INT         NOT NULL,
        CarteraID         INT         NOT NULL,
        Fuente            VARCHAR(30) NOT NULL,   -- clave del Cargue de Fuentes (Fosyga, DatosClaro...)
        Total             INT         NOT NULL,
        Buscados          INT         NOT NULL,
        Encontrados       INT         NOT NULL,
        Viejos90          INT         NOT NULL,
        Viejos180         INT         NOT NULL,
        Viejos365         INT         NOT NULL,
        UltimaConsultaMax DATE        NULL,       -- consulta más reciente del par en la fuente

        CONSTRAINT PK_LocalizacionResumenFuente PRIMARY KEY CLUSTERED (CorteID, LiderID, CarteraID, Fuente),
        CONSTRAINT FK_LocalizacionResumenFuente_Corte FOREIGN KEY (CorteID)
            REFERENCES dbo.LocalizacionCorte (CorteID) ON DELETE CASCADE,
        CONSTRAINT CK_LocalizacionResumenFuente_Conteos CHECK (
            Buscados <= Total AND Encontrados <= Buscados
            AND Viejos90 <= Buscados AND Viejos180 <= Viejos90 AND Viejos365 <= Viejos180)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 3. Resumen por líder y fuente (titulares distintos)
--
--    Un titular que está en dos carteras del mismo líder cuenta una vez.
--    LiderID = 0 es el total de todos los pares activos (titulares distintos
--    de toda la base), lo que ven Admin y Director sin filtro.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionResumenFuenteLider', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionResumenFuenteLider
    (
        CorteID           INT         NOT NULL,
        LiderID           INT         NOT NULL,   -- 0 = todos
        Fuente            VARCHAR(30) NOT NULL,
        Total             INT         NOT NULL,
        Buscados          INT         NOT NULL,
        Encontrados       INT         NOT NULL,
        Viejos90          INT         NOT NULL,
        Viejos180         INT         NOT NULL,
        Viejos365         INT         NOT NULL,
        UltimaConsultaMax DATE        NULL,

        CONSTRAINT PK_LocalizacionResumenFuenteLider PRIMARY KEY CLUSTERED (CorteID, LiderID, Fuente),
        CONSTRAINT FK_LocalizacionResumenFuenteLider_Corte FOREIGN KEY (CorteID)
            REFERENCES dbo.LocalizacionCorte (CorteID) ON DELETE CASCADE,
        CONSTRAINT CK_LocalizacionResumenFuenteLider_Conteos CHECK (
            Buscados <= Total AND Encontrados <= Buscados
            AND Viejos90 <= Buscados AND Viejos180 <= Viejos90 AND Viejos365 <= Viejos180)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 4. Resumen por par (métricas que cruzan fuentes)
--
--    ConContacto          celular o email en la última fila de alguna fuente:
--                         Total - SinContacto del Calificador.
--    ConContactoFuncional lo mismo, solo en fuentes Funcionales.
--    BuscadosAlguna       con fila en alguna fuente del panel.
--    BuscadosFuncional    con fila en alguna fuente Funcional.
--    Frescos180           su consulta más reciente (cualquier fuente) tiene 180 días o menos.
--    ConSaldo             UltimoEstadoTitular.SaldoTotal > 0 en la cartera del par.
--    SinContactoConSaldo  con saldo y sin contacto; SaldoSinContacto = su saldo.
--    Interna              relación no externa y ni líder ni cartera "BASE GENERAL"
--                         (lo que excluyen los rankings por defecto).
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionResumenPar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionResumenPar
    (
        CorteID              INT           NOT NULL,
        LiderID              INT           NOT NULL,
        CarteraID            INT           NOT NULL,
        Interna              BIT           NOT NULL,
        Total                INT           NOT NULL,
        ConContacto          INT           NOT NULL,
        ConContactoFuncional INT           NOT NULL,
        BuscadosAlguna       INT           NOT NULL,
        BuscadosFuncional    INT           NOT NULL,
        Frescos180           INT           NOT NULL,
        ConSaldo             INT           NOT NULL,
        SinContactoConSaldo  INT           NOT NULL,
        SaldoSinContacto     DECIMAL(19,2) NOT NULL,
        SaldoTotal           DECIMAL(19,2) NOT NULL,

        CONSTRAINT PK_LocalizacionResumenPar PRIMARY KEY CLUSTERED (CorteID, LiderID, CarteraID),
        CONSTRAINT FK_LocalizacionResumenPar_Corte FOREIGN KEY (CorteID)
            REFERENCES dbo.LocalizacionCorte (CorteID) ON DELETE CASCADE
    );
END
GO

-- ----------------------------------------------------------------------------
-- 5. Resumen por líder (titulares distintos)
--
--    SoloInternas = 0: todos los pares activos del líder.
--    SoloInternas = 1: solo sus pares internos (sin relaciones externas ni
--                      "BASE GENERAL"): el universo por defecto de los rankings.
--    LiderID = 0 es el total de la base (titulares distintos).
--    El saldo de un titular es la suma de sus carteras del líder.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionResumenLider', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionResumenLider
    (
        CorteID              INT           NOT NULL,
        LiderID              INT           NOT NULL,   -- 0 = todos
        SoloInternas         BIT           NOT NULL,
        Total                INT           NOT NULL,
        ConContacto          INT           NOT NULL,
        ConContactoFuncional INT           NOT NULL,
        BuscadosAlguna       INT           NOT NULL,
        BuscadosFuncional    INT           NOT NULL,
        Frescos180           INT           NOT NULL,
        ConSaldo             INT           NOT NULL,
        SinContactoConSaldo  INT           NOT NULL,
        SaldoSinContacto     DECIMAL(19,2) NOT NULL,
        SaldoTotal           DECIMAL(19,2) NOT NULL,

        CONSTRAINT PK_LocalizacionResumenLider PRIMARY KEY CLUSTERED (CorteID, LiderID, SoloInternas),
        CONSTRAINT FK_LocalizacionResumenLider_Corte FOREIGN KEY (CorteID)
            REFERENCES dbo.LocalizacionCorte (CorteID) ON DELETE CASCADE
    );
END
GO

-- ----------------------------------------------------------------------------
-- 6. Envíos a localización (etapa 3)
--
--    Un lote = un par (líder, cartera) y una fuente. Código visible LOC-000123.
--    Estados: BORRADOR -> ENVIADO -> CERRADO, o ANULADO desde BORRADOR/ENVIADO.
--    CargueEjecucionID: el cargue OK de esa fuente que se tomó como retorno.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionEnvio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionEnvio
    (
        EnvioID           INT            IDENTITY(1,1) NOT NULL,
        Codigo            AS ('LOC-' + RIGHT('000000' + CAST(EnvioID AS VARCHAR(10)), 6)),   -- calculada (no persistida): LOC-000123
        LiderID           INT            NOT NULL,
        CarteraID         INT            NOT NULL,
        Fuente            VARCHAR(30)    NOT NULL,   -- clave del Cargue de Fuentes; solo fuentes Funcionales
        Estado            VARCHAR(10)    NOT NULL CONSTRAINT DF_LocalizacionEnvio_Estado DEFAULT ('BORRADOR'),
        Titulares         INT            NOT NULL CONSTRAINT DF_LocalizacionEnvio_Titulares DEFAULT (0),
        Parametros        NVARCHAR(1000) NULL,       -- JSON con los filtros de la sugerencia que originó el lote
        Observacion       NVARCHAR(500)  NULL,
        CreadoPor         VARCHAR(100)   NOT NULL,
        FechaCreacion     DATETIME2(0)   NOT NULL CONSTRAINT DF_LocalizacionEnvio_Creacion DEFAULT (SYSDATETIME()),
        EnviadoPor        VARCHAR(100)   NULL,
        FechaEnvio        DATETIME2(0)   NULL,
        CerradoPor        VARCHAR(100)   NULL,
        FechaCierre       DATETIME2(0)   NULL,
        AnuladoPor        VARCHAR(100)   NULL,
        FechaAnulacion    DATETIME2(0)   NULL,
        MotivoAnulacion   NVARCHAR(300)  NULL,
        CargueEjecucionID BIGINT         NULL,       -- CargueFuenteEjecucion que devolvió el lote (retorno)
        FechaRetorno      DATETIME2(0)   NULL,

        CONSTRAINT PK_LocalizacionEnvio PRIMARY KEY CLUSTERED (EnvioID),
        CONSTRAINT CK_LocalizacionEnvio_Estado CHECK (Estado IN ('BORRADOR', 'ENVIADO', 'CERRADO', 'ANULADO')),
        -- Cada estado trae su marca de tiempo.
        CONSTRAINT CK_LocalizacionEnvio_Fechas CHECK (
                (Estado <> 'ENVIADO' OR FechaEnvio IS NOT NULL)
            AND (Estado <> 'CERRADO' OR (FechaEnvio IS NOT NULL AND FechaCierre IS NOT NULL))
            AND (Estado <> 'ANULADO' OR FechaAnulacion IS NOT NULL))
    );
    -- Lotes de un par/fuente (pantalla de envíos, "enviados en los últimos 60 días").
    CREATE NONCLUSTERED INDEX IX_LocalizacionEnvio_Par
        ON dbo.LocalizacionEnvio (LiderID, CarteraID, Fuente, Estado) INCLUDE (FechaEnvio);
    CREATE NONCLUSTERED INDEX IX_LocalizacionEnvio_Estado
        ON dbo.LocalizacionEnvio (Estado, FechaEnvio);
END
GO

-- El retorno apunta a la auditoría del Cargue de Fuentes (ADR-039), si ya existe.
IF OBJECT_ID('dbo.LocalizacionEnvio', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.CargueFuenteEjecucion', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.FK_LocalizacionEnvio_CargueEjecucion', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.LocalizacionEnvio
        ADD CONSTRAINT FK_LocalizacionEnvio_CargueEjecucion
            FOREIGN KEY (CargueEjecucionID) REFERENCES dbo.CargueFuenteEjecucion (EjecucionID);
END
GO

-- ----------------------------------------------------------------------------
-- 7. Detalle de envíos (etapa 3)
--
--    Un titular por lote, con la foto de UltimoEstadoTitular y de la fuente al
--    momento de crear el lote: permite medir retorno e impacto sin depender de
--    cómo cambió el titular después.
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.LocalizacionEnvioDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocalizacionEnvioDetalle
    (
        EnvioID              INT          NOT NULL,
        TitularID            BIGINT       NOT NULL,
        Categoria            VARCHAR(20)  NOT NULL,  -- NUNCA_BUSCADO | NO_ENC_VIEJO | ENC_VIEJO
        Prioridad            SMALLINT     NOT NULL,  -- puntaje explicable de la sugerencia
        UltimaConsultaPrevia DATE         NULL,      -- última consulta en la fuente antes del envío (NULL = nunca)
        SaldoTotal           MONEY        NULL,      -- foto de UltimoEstadoTitular (par) al crear el lote
        UltimoPerfil         VARCHAR(50)  NULL,
        MejorPerfil          VARCHAR(50)  NULL,
        FechaUltimaGestion   DATE         NULL,
        FechaPromesa         DATE         NULL,
        ValorPromesa         MONEY        NULL,

        CONSTRAINT PK_LocalizacionEnvioDetalle PRIMARY KEY CLUSTERED (EnvioID, TitularID),
        CONSTRAINT FK_LocalizacionEnvioDetalle_Envio FOREIGN KEY (EnvioID)
            REFERENCES dbo.LocalizacionEnvio (EnvioID) ON DELETE CASCADE,
        CONSTRAINT CK_LocalizacionEnvioDetalle_Categoria
            CHECK (Categoria IN ('NUNCA_BUSCADO', 'NO_ENC_VIEJO', 'ENC_VIEJO'))
    );
    -- "Excluir enviados en los últimos 60 días" busca por titular.
    CREATE NONCLUSTERED INDEX IX_LocalizacionEnvioDetalle_Titular
        ON dbo.LocalizacionEnvioDetalle (TitularID) INCLUDE (EnvioID);
END
GO

-- Verificación
SELECT name, create_date
  FROM sys.tables
 WHERE name IN ('LocalizacionCorte', 'LocalizacionResumenFuente', 'LocalizacionResumenFuenteLider',
                'LocalizacionResumenPar', 'LocalizacionResumenLider',
                'LocalizacionEnvio', 'LocalizacionEnvioDetalle')
 ORDER BY name;
GO

-- ----------------------------------------------------------------------------
-- Rollback (NO ejecutar salvo para revertir esta migración; se pierden el
-- historial de cortes y los lotes de envío):
--
-- DROP TABLE IF EXISTS dbo.LocalizacionEnvioDetalle;
-- DROP TABLE IF EXISTS dbo.LocalizacionEnvio;
-- DROP TABLE IF EXISTS dbo.LocalizacionResumenLider;
-- DROP TABLE IF EXISTS dbo.LocalizacionResumenPar;
-- DROP TABLE IF EXISTS dbo.LocalizacionResumenFuenteLider;
-- DROP TABLE IF EXISTS dbo.LocalizacionResumenFuente;
-- DROP TABLE IF EXISTS dbo.LocalizacionCorte;
-- ----------------------------------------------------------------------------

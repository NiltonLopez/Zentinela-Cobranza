-- ============================================================================
-- Avisos del ETL de estado de titulares — DBCallCenter (ADR-038)
--
--   Registro de los avisos ya enviados, para no repetir el mismo aviso cada
--   media hora. Una alerta que se repite deja de leerse, y entonces el día que
--   importe tampoco se va a leer.
--
--   Lo consume la skill etl-estado-titular y el vigía del Agente.
--
--   Idempotente. Aplicar con: sqlcmd -f 65001 -i <este archivo>
--   Fecha: 2026-09-23.
-- ============================================================================

USE DBCallCenter;
GO

IF OBJECT_ID('dbo.EtlEstadoTitular_Aviso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EtlEstadoTitular_Aviso
    (
        AvisoID        INT           IDENTITY(1,1) NOT NULL,
        FechaEnvio     DATETIME2(3)  NOT NULL CONSTRAINT DF_EtlAviso_Fecha DEFAULT (SYSDATETIME()),
        -- Clasificación del incidente. Si cambia, se vuelve a avisar aunque sea
        -- reciente: pasar de 'RANCIO' a 'CRITICO' es noticia.
        Estado         VARCHAR(12)   NOT NULL,
        HorasAtraso    INT           NULL,
        Mensaje        NVARCHAR(1200) NOT NULL,
        Enviado        BIT           NOT NULL CONSTRAINT DF_EtlAviso_Enviado DEFAULT (1),
        DetalleError   NVARCHAR(400) NULL,   -- por qué no se pudo enviar
        Origen         VARCHAR(30)   NOT NULL CONSTRAINT DF_EtlAviso_Origen DEFAULT ('Skill'),

        CONSTRAINT PK_EtlEstadoTitular_Aviso PRIMARY KEY CLUSTERED (AvisoID),
        CONSTRAINT CK_EtlAviso_Estado CHECK (Estado IN ('ATENCION', 'RANCIO', 'CRITICO', 'RECUPERADO'))
    );

    CREATE NONCLUSTERED INDEX IX_EtlAviso_Fecha ON dbo.EtlEstadoTitular_Aviso (FechaEnvio DESC)
        INCLUDE (Estado, Enviado);
END
GO

-- ----------------------------------------------------------------------------
-- ¿Corresponde avisar?
--
--   Devuelve 1 solo si hay algo que decir Y no se dijo hace poco. La regla:
--   se avisa si cambió el estado, o si pasaron más de @HorasSilencio horas
--   desde el último aviso del mismo estado.
-- ----------------------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fn_EtlEstadoTitular_DebeAvisar
(
    @Estado         VARCHAR(12),
    @HorasSilencio  INT = 12
)
RETURNS BIT
AS
BEGIN
    IF @Estado IS NULL RETURN 0;

    DECLARE @ultEstado VARCHAR(12), @ultFecha DATETIME2(3);

    SELECT TOP (1) @ultEstado = Estado, @ultFecha = FechaEnvio
    FROM dbo.EtlEstadoTitular_Aviso
    WHERE Enviado = 1
    ORDER BY AvisoID DESC;

    IF @ultEstado IS NULL            RETURN 1;   -- nunca se aviso
    IF @ultEstado <> @Estado         RETURN 1;   -- cambio la situacion
    IF DATEDIFF(HOUR, @ultFecha, SYSDATETIME()) >= @HorasSilencio RETURN 1;

    RETURN 0;
END
GO

-- ----------------------------------------------------------------------------
-- Registrar un aviso (enviado o fallido)
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_EtlEstadoTitular_RegistrarAviso
    @Estado       VARCHAR(12),
    @Mensaje      NVARCHAR(1200),
    @HorasAtraso  INT           = NULL,
    @Enviado      BIT           = 1,
    @DetalleError NVARCHAR(400) = NULL,
    @Origen       VARCHAR(30)   = 'Skill'
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.EtlEstadoTitular_Aviso
        (Estado, Mensaje, HorasAtraso, Enviado, DetalleError, Origen)
    VALUES
        (@Estado, @Mensaje, @HorasAtraso, @Enviado, @DetalleError, @Origen);

    SELECT SCOPE_IDENTITY() AS AvisoID;
END
GO

-- ----------------------------------------------------------------------------
-- Diagnóstico en una fila: estado + si toca avisar
--
--   Los umbrales son de frescura DIARIA (el ETL corre una vez al día): 26 h es
--   "se saltó una corrida", 42 h es "ya van dos". Para vigilar una corrida en
--   progreso, el umbral corto va por parámetro en la skill.
-- ----------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.vw_EtlEstadoTitularAlerta
AS
SELECT
    e.UltimaCargaOk,
    e.HorasDesdeCargaOk,
    e.FilasUltimaCargaOk,
    e.CarterasUltimaCargaOk,
    e.UltimoIntento,
    e.ResultadoUltimoIntento,
    e.MotivoUltimoIntento,
    f.FilasActuales,
    CASE
        WHEN f.FilasActuales = 0                                               THEN 'CRITICO'
        WHEN e.FilasUltimaCargaOk IS NOT NULL
             AND f.FilasActuales < e.FilasUltimaCargaOk / 2                    THEN 'CRITICO'
        WHEN e.HorasDesdeCargaOk IS NULL                                       THEN 'CRITICO'
        WHEN e.HorasDesdeCargaOk > 42                                          THEN 'RANCIO'
        WHEN e.HorasDesdeCargaOk > 26                                          THEN 'ATENCION'
        ELSE 'OK'
    END AS Estado
FROM dbo.vw_EtlEstadoTitularEstado e
CROSS JOIN (SELECT COUNT(*) AS FilasActuales FROM dbo.UltimoEstadoTitular) f;
GO

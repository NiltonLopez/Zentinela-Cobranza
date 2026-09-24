-- ============================================================================
-- Cargador de UltimoEstadoTitular — objetos de base (ADR-039)
--
--   EtlEstadoTitular_Rechazo    las filas que el cargador aparto, y por que.
--
--   En el paquete SSIS esas filas no dejaban rastro: o tumbaban la carga entera
--   (cartera sin registrar, titular que no convierte a numero) o se perdian en
--   silencio (tipo de cartera inactivo: el destino las mandaba a una salida de
--   error sin conectar). Ahora se apartan, se cuentan y quedan aca para que alguien
--   pueda responder "que no se cargo hoy".
--
--   Solo se guarda donde esta la fila (archivo y linea del CSV) y el motivo. Nada
--   de datos personales: para ver el registro hay que ir al CSV del dia.
--
--   Retencion: 30 dias. La purga la hace el propio cargador en cada corrida.
--
--   Requiere haber aplicado 2026-09-23_rediseno_etl_estado_titular.sql (en su
--   version con la compuerta que toma el universo de Cartera).
--   Idempotente. Aplicar con: sqlcmd -f 65001 -i <este archivo>
--   Fecha: 2026-09-25.
-- ============================================================================

USE DBCallCenter;
GO

IF OBJECT_ID('dbo.EtlEstadoTitular_Rechazo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EtlEstadoTitular_Rechazo
    (
        RechazoID   BIGINT        IDENTITY(1,1) NOT NULL,
        FechaCarga  DATETIME2(3)  NOT NULL CONSTRAINT DF_EtlRechazo_Fecha DEFAULT (SYSDATETIME()),
        Archivo     NVARCHAR(100) NOT NULL,   -- cartera segun el nombre del CSV
        Linea       INT           NOT NULL,   -- registro dentro del CSV (1 = encabezado)
        -- titular-invalido | cartera-sin-registrar | tipo-cartera-inactiva
        Motivo      VARCHAR(40)   NOT NULL,
        TipoCartera VARCHAR(100)  NULL,       -- el Tipo_Cartera que no se encontro, si aplica

        CONSTRAINT PK_EtlEstadoTitular_Rechazo PRIMARY KEY CLUSTERED (RechazoID)
    );

    CREATE NONCLUSTERED INDEX IX_EtlRechazo_Fecha
        ON dbo.EtlEstadoTitular_Rechazo (FechaCarga DESC)
        INCLUDE (Archivo, Motivo, TipoCartera);
END
GO

-- Resumen listo para leer o para el aviso: la ultima carga, agrupada.
CREATE OR ALTER VIEW dbo.vw_EtlEstadoTitularRechazos
AS
SELECT r.Motivo, r.Archivo, r.TipoCartera, COUNT(*) AS Filas, MAX(r.FechaCarga) AS FechaCarga
FROM dbo.EtlEstadoTitular_Rechazo r
WHERE r.FechaCarga >= (SELECT DATEADD(MINUTE, -30, MAX(FechaCarga)) FROM dbo.EtlEstadoTitular_Rechazo)
GROUP BY r.Motivo, r.Archivo, r.TipoCartera;
GO

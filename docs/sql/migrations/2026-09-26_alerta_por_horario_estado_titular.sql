-- ============================================================================
-- Alerta de frescura según el HORARIO del ETL de estado de titulares
--
--   Horario (2026-09-26): lunes a viernes 7, 9, 11, 13, 15 y 17 h; sábado 7, 9 y 11 h;
--   domingo nada.
--
--   Antes la alerta usaba umbrales fijos de carga diaria (26 h / 42 h). Con este
--   horario fallaba en los dos sentidos: entre semana tardaba un día en notar que
--   las corridas no pasaban, y el fin de semana (sábado 11 h -> lunes 7 h = 44 h)
--   daba RANCIO sin que se hubiera perdido nada.
--
--   Ahora se cuentan las corridas PROGRAMADAS que pasaron sin una carga OK después:
--       0 -> OK · 1 -> ATENCION · 2 o más -> RANCIO
--   Cada corrida tiene @GraciaMin minutos para terminar antes de contarse perdida
--   (descarga + carga). De noche y el domingo no hay corridas que perder.
--
--   Si cambia el horario, cambiarlo ACÁ y en el trabajo del Agente
--   (2026-09-26_horario_cargador_estado_titular.sql).
--
--   No contempla festivos: un lunes festivo sin corridas daría ATENCION/RANCIO.
-- ============================================================================
USE DBCallCenter;
GO

CREATE OR ALTER FUNCTION dbo.fn_EtlEstadoTitular_CorridasPerdidas
(
    @UltimaOk   DATETIME2(3),
    @Ahora      DATETIME2(3),
    @GraciaMin  INT
)
RETURNS TABLE
AS
RETURN
WITH Dias AS (
    -- Un día por fila desde la última carga OK hasta hoy (tope 60: más atrás ya es RANCIO igual)
    SELECT TOP (CASE WHEN DATEDIFF(DAY, @UltimaOk, @Ahora) + 1 > 60 THEN 60
                     ELSE DATEDIFF(DAY, @UltimaOk, @Ahora) + 1 END)
           DATEADD(DAY, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, CAST(@UltimaOk AS DATE)) AS Dia
    FROM sys.all_objects
),
Corridas AS (
    SELECT DATEADD(HOUR, h.Hora, CAST(d.Dia AS DATETIME2(3))) AS Corrida,
           h.Hora,
           -- 0 = lunes ... 6 = domingo, sin depender de SET DATEFIRST ni del idioma
           DATEDIFF(DAY, '19000101', d.Dia) % 7 AS DiaSemana
    FROM Dias d
    CROSS JOIN (VALUES (7), (9), (11), (13), (15), (17)) h(Hora)
)
SELECT COUNT(*) AS Perdidas
FROM Corridas
WHERE Corrida > @UltimaOk
  AND DATEADD(MINUTE, @GraciaMin, Corrida) <= @Ahora
  AND (DiaSemana BETWEEN 0 AND 4 OR (DiaSemana = 5 AND Hora <= 11));
GO

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
    p.Perdidas AS CorridasPerdidas,
    CASE
        WHEN f.FilasActuales = 0                                               THEN 'CRITICO'
        WHEN e.FilasUltimaCargaOk IS NOT NULL
             AND f.FilasActuales < e.FilasUltimaCargaOk / 2                    THEN 'CRITICO'
        WHEN e.HorasDesdeCargaOk IS NULL                                       THEN 'CRITICO'
        WHEN p.Perdidas >= 2                                                   THEN 'RANCIO'
        WHEN p.Perdidas = 1                                                    THEN 'ATENCION'
        ELSE 'OK'
    END AS Estado
FROM dbo.vw_EtlEstadoTitularEstado e
CROSS JOIN (SELECT COUNT(*) AS FilasActuales FROM dbo.UltimoEstadoTitular) f
OUTER APPLY dbo.fn_EtlEstadoTitular_CorridasPerdidas(
    e.UltimaCargaOk, CAST(SYSDATETIME() AS DATETIME2(3)), 60) p;
GO

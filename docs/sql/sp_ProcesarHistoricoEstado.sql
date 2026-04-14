-- ============================================================
-- sp_ProcesarHistoricoEstado
-- INSERT append desde ReporteEstadoAuxiliar → HistoricoEstadoTitular.
-- Solo inserta filas cuya PK (TitularID+CarteraID+AgenteID) no exista.
-- Nunca elimina ni actualiza registros históricos.
-- Ejecutar en: DBCallCenter
-- ============================================================
CREATE OR ALTER PROCEDURE sp_ProcesarHistoricoEstado
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO HistoricoEstadoTitular (
        TitularID, CarteraID, AgenteID,
        UltimoPerfil, MejorPerfil, FechaMejorPerfil,
        SaldoTotal, NumeroMarcado, Rotacion,
        FechaUltimaGestion, FechaPromesa, ValorPromesa,
        MotivoNoPago
    )
    SELECT
        src.TitularID, src.CarteraID, src.AgenteID,
        src.UltimoPerfil, src.MejorPerfil, src.FechaMejorPerfil,
        src.SaldoTotal, src.NumeroMarcado, src.Rotacion,
        src.FechaUltimaGestion, src.FechaPromesa, src.ValorPromesa,
        src.MotivoNoPago
    FROM ReporteEstadoAuxiliar src
    WHERE src.TitularID IS NOT NULL
      AND src.CarteraID IS NOT NULL
      AND src.AgenteID  IS NOT NULL
      AND NOT EXISTS (
              SELECT 1
              FROM   HistoricoEstadoTitular h
              WHERE  h.TitularID = src.TitularID
                AND  h.CarteraID = src.CarteraID
                AND  h.AgenteID  = src.AgenteID
          );
END;

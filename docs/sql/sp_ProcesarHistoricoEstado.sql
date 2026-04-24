-- ============================================================
-- sp_ProcesarHistoricoEstado
-- INSERT append desde ReporteEstadoAuxiliar → HistoricoEstadoTitular.
-- Solo inserta filas cuya PK (TitularID+CarteraID+AgenteID) no exista.
-- Nunca elimina ni actualiza registros históricos.
-- Ejecutar en: DBCallCenter
-- NOTA: FechaMejorPerfil eliminado (campo sin valor operativo).
-- ============================================================
CREATE OR ALTER PROCEDURE sp_ProcesarHistoricoEstado
AS
BEGIN
    SET NOCOUNT ON;

    MERGE HistoricoEstadoTitular WITH (HOLDLOCK) AS tgt
    USING (
        SELECT TitularID, CarteraID, AgenteID,
               UltimoPerfil, MejorPerfil,
               SaldoTotal, NumeroMarcado, Rotacion,
               FechaUltimaGestion, FechaPromesa, ValorPromesa,
               MotivoNoPago,
               ISNULL(FechaActualizacion, GETDATE()) AS FechaActualizacion
        FROM   ReporteEstadoAuxiliar
        WHERE  TitularID IS NOT NULL
          AND  CarteraID IS NOT NULL
          AND  AgenteID  IS NOT NULL
    ) AS src
        ON  tgt.TitularID = src.TitularID
        AND tgt.CarteraID = src.CarteraID
        AND tgt.AgenteID  = src.AgenteID
        AND ISNULL(tgt.Rotacion, -1) = ISNULL(src.Rotacion, -1)
    WHEN MATCHED THEN
        UPDATE SET tgt.FechaActualizacion = src.FechaActualizacion
    WHEN NOT MATCHED THEN
        INSERT (TitularID, CarteraID, AgenteID,
                UltimoPerfil, MejorPerfil,
                SaldoTotal, NumeroMarcado, Rotacion,
                FechaUltimaGestion, FechaPromesa, ValorPromesa,
                MotivoNoPago, FechaActualizacion)
        VALUES (src.TitularID, src.CarteraID, src.AgenteID,
                src.UltimoPerfil, src.MejorPerfil,
                src.SaldoTotal, src.NumeroMarcado, src.Rotacion,
                src.FechaUltimaGestion, src.FechaPromesa, src.ValorPromesa,
                src.MotivoNoPago, src.FechaActualizacion);
END;

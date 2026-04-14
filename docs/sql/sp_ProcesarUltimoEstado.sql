-- ============================================================
-- sp_ProcesarUltimoEstado
-- 1. Inserta titulares nuevos que no existan en Titular.
-- 2. UPSERT desde ReporteEstadoAuxiliar → UltimoEstadoTitular.
-- Ejecutar en: DBCallCenter
-- ============================================================
CREATE OR ALTER PROCEDURE sp_ProcesarUltimoEstado
AS
BEGIN
    SET NOCOUNT ON;

    -- Paso 1: crear titulares nuevos (solo los que tienen Nombre y no existen)
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT src.TitularID, src.Nombre
    FROM   ReporteEstadoAuxiliar src
    WHERE  src.TitularID IS NOT NULL
      AND  src.Nombre    IS NOT NULL
      AND  NOT EXISTS (
               SELECT 1 FROM Titular t
               WHERE t.NumeroDocumento = src.TitularID
           );

    -- Paso 2: UPSERT sobre UltimoEstadoTitular
    MERGE UltimoEstadoTitular AS tgt
    USING (
        SELECT TitularID, CarteraID, AgenteID,
               UltimoPerfil, MejorPerfil, FechaMejorPerfil,
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
    WHEN MATCHED THEN
        UPDATE SET
            tgt.UltimoPerfil       = src.UltimoPerfil,
            tgt.MejorPerfil        = src.MejorPerfil,
            tgt.FechaMejorPerfil   = src.FechaMejorPerfil,
            tgt.SaldoTotal         = src.SaldoTotal,
            tgt.NumeroMarcado      = src.NumeroMarcado,
            tgt.Rotacion           = src.Rotacion,
            tgt.FechaUltimaGestion = src.FechaUltimaGestion,
            tgt.FechaPromesa       = src.FechaPromesa,
            tgt.ValorPromesa       = src.ValorPromesa,
            tgt.MotivoNoPago       = src.MotivoNoPago,
            tgt.FechaActualizacion = src.FechaActualizacion
    WHEN NOT MATCHED THEN
        INSERT (TitularID, CarteraID, AgenteID,
                UltimoPerfil, MejorPerfil, FechaMejorPerfil,
                SaldoTotal, NumeroMarcado, Rotacion,
                FechaUltimaGestion, FechaPromesa, ValorPromesa,
                MotivoNoPago, FechaActualizacion)
        VALUES (src.TitularID, src.CarteraID, src.AgenteID,
                src.UltimoPerfil, src.MejorPerfil, src.FechaMejorPerfil,
                src.SaldoTotal, src.NumeroMarcado, src.Rotacion,
                src.FechaUltimaGestion, src.FechaPromesa, src.ValorPromesa,
                src.MotivoNoPago, src.FechaActualizacion);
END;

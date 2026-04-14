-- ============================================================
-- sp_ProcesarAsignacionCartera
-- Transfiere las filas de Titular_CarteraAuxiliar a Titular_Cartera
-- para la combinación @CarteraID / @LiderID indicada.
-- Solo inserta filas que no estén ya presentes (idempotente).
-- Ejecutar en: DBCallCenter
-- ============================================================
CREATE OR ALTER PROCEDURE sp_ProcesarAsignacionCartera
    @CarteraID INT,
    @LiderID   INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Reemplaza la asignación completa: elimina la lista anterior y carga la nueva.
    DELETE FROM Titular_Cartera
    WHERE  CarteraID = @CarteraID
      AND  LiderID   = @LiderID;

    INSERT INTO Titular_Cartera (TitularID, LiderID, CarteraID)
    SELECT a.TitularID, @LiderID, @CarteraID
    FROM   Titular_CarteraAuxiliar a
    WHERE  a.CarteraID = @CarteraID;
END;

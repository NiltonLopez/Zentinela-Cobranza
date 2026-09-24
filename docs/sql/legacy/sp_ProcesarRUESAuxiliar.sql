CREATE PROCEDURE [dbo].[sp_ProcesarRUESAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ra.TitularID, ra.Nombre
    FROM RUESAuxiliar ra
    LEFT JOIN Titular t ON ra.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

    PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ra.TitularID, ra.CarteraID, ra.LiderID
    FROM RUESAuxiliar ra
    LEFT JOIN Titular_Cartera tc 
        ON ra.TitularID = tc.TitularID AND ra.CarteraID = tc.CarteraID AND ra.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

    PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en RUESAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM RUESAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	----------------------------------------
    -- 4. Update el campo EstadoEnRUES en RUESAuxiliar
    ----------------------------------------

	UPDATE RUESAuxiliar
    SET EstadoEnRUES = CASE 
        WHEN NumeroMatricula IS NULL AND EstadoEmpresa IS NULL AND ActividadEconomica IS NULL THEN 0
        ELSE 1
    END;

    ----------------------------------------------------
	-- 5. Update el campo FechaConsulta en RUESAuxiliar
    ----------------------------------------------------

	UPDATE RUESAuxiliar
    SET FechaConsulta = GETDATE();

    ------------------------------
    -- 6. Insertar en RUES
    -----------------------------
    INSERT INTO RUES
    SELECT ra.TitularID, ra.NumeroMatricula, ra.EstadoEmpresa, ra.ActividadEconomica, ra.NombreEmpresa, ra.EstadoEnRUES, ra.FechaConsulta
    FROM RUESAuxiliar ra;


	-------------------------------------------
	-- 7. Eliminar duplicados en RUES
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NumeroMatricula, EstadoEmpresa, ActividadEconomica, NombreEmpresa ORDER BY FechaConsulta DESC) AS rn
        FROM RUES
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

    	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS DE RUES';

	
END;

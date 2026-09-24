
CREATE PROCEDURE [dbo].[sp_ProcesarRGMAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT fa.TitularID, fa.Nombre
    FROM RGMAuxiliar fa
    LEFT JOIN Titular t ON fa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID,LiderID)
    SELECT DISTINCT fa.TitularID, fa.CarteraID,fa.CarteraID
    FROM RGMAuxiliar fa
    LEFT JOIN Titular_Cartera tc 
        ON fa.TitularID = tc.TitularID AND fa.CarteraID = tc.CarteraID AND fa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS';


    ------------------------------
    -- 3. Eliminar duplicados en RGMAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM RGMAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	
	 ---------------------------------------
    -- 4. Update el campo EstadoEnRGM en RGMAuxiliar
    ----------------------------------------

	UPDATE RGMAuxiliar
    SET EstadoEnRGM = CASE 
        WHEN Marca IS NULL AND Modelo IS NULL AND Placa IS NULL THEN 0
        ELSE 1
    END;


    ---------------------------------------
    -- 5. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE RGMAuxiliar
	SET FechaConsulta = GETDATE()


    ------------------------------
    -- 6. Insertar en RGM
    -----------------------------
    INSERT INTO RGM
    SELECT fa.TitularID, fa.Marca, fa.Modelo, fa.Placa, fa.EstadoEnRGM, fa.FechaConsulta
    FROM RGMAuxiliar fa;


	-------------------------------------------
	-- 7. Eliminar duplicados en RGM
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, Marca, Modelo, Placa ORDER BY FechaConsulta DESC) AS rn
        FROM RGM
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS DE RGM';
	
END;

CREATE PROCEDURE [dbo].[sp_ProcesarArlSuraAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM ARLSuraAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM ARLSuraAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID AND ca.CarteraID = tc.CarteraID AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en ArlSuraAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM ARLSuraAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	 ---------------------------------------
    -- 4. Update el campo EstadoEnArlSura en ArlSuraAuxiliar
    ----------------------------------------

	UPDATE ARLSuraAuxiliar
    SET EstadoEnSura = CASE 
        WHEN Empresa IS NULL AND NIT IS NULL THEN 0
        ELSE 1
    END;

	---------------------------------------
    -- 5. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE ARLSuraAuxiliar
	SET FechaConsulta = GETDATE()


    -----------------------------
    -- 6. Insertar en ArlSura
    -----------------------------
    INSERT INTO ArlSura
    SELECT ca.TitularID,ca.NIT, ca.Empresa,ca.EstadoEnSura, ca.FechaConsulta
    FROM ArlSuraAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A SURA';

	-------------------------------------------
	-- 7. Eliminar duplicados en ArlSura
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, Nit ORDER BY FechaConsulta DESC) AS rn
        FROM ArlSura
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';
	
END;

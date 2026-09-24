CREATE PROCEDURE [dbo].[sp_ProcesarCompensadosAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM CompensadosAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM CompensadosAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID AND ca.CarteraID = tc.CarteraID AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS';


    ------------------------------
    -- 3. Eliminar duplicados en CompensadosAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM CompensadosAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	
	 ---------------------------------------
    -- 4. Update el campo EstadoEnCompensados en CompensadosAuxiliar
    ----------------------------------------

	UPDATE CompensadosAuxiliar
    SET Estado = CASE 
        WHEN Estado IS NULL AND EPS IS NULL AND UltimoPeriodoCompensado IS NULL THEN 0
        ELSE 1
    END;


    ---------------------------------------
    -- 5. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE CompensadosAuxiliar
	SET FechaConsulta = GETDATE()

    ------------------------------
    -- 7. Insertar en Compensados
    -----------------------------
    INSERT INTO Compensados
    SELECT fa.TitularID, fa.EPS, fa.UltimoPeriodoCompensado, fa.TipoAfiliado, fa.Estado, fa.FechaConsulta
    FROM CompensadosAuxiliar fa;


	-------------------------------------------
	-- 8. Eliminar duplicados en Compensados
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, EPS, UltimoPeriodoCompensado, TipoAfiliado ORDER BY FechaConsulta DESC) AS rn
        FROM Compensados
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS DE COMPENSADOS';
	
END;

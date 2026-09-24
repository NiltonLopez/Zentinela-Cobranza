CREATE PROCEDURE [dbo].[sp_ProcesarFosygaAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT fa.TitularID, fa.Nombre
    FROM FosygaAuxiliar fa
    LEFT JOIN Titular t ON fa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT fa.TitularID, fa.CarteraID, fa.LiderID
    FROM FosygaAuxiliar fa
    LEFT JOIN Titular_Cartera tc 
        ON fa.TitularID = tc.TitularID AND fa.CarteraID = tc.CarteraID AND fa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS';


    ------------------------------
    -- 3. Eliminar duplicados en FosygaAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM FosygaAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	
	 ---------------------------------------
    -- 4. Update el campo TipoAfiliado en FosygaAuxiliar
    ----------------------------------------

	UPDATE FosygaAuxiliar
    SET TipoAfiliado = 'FALLECIDO' 
	WHERE Estado = 'AFILIADO FALLECIDO';

	 ---------------------------------------
    -- 5. Update el campo EstadoEnFosyga en FosygaAuxiliar
    ----------------------------------------

	UPDATE FosygaAuxiliar
    SET EstadoEnFosyga = CASE 
        WHEN Estado IS NULL AND EPS IS NULL AND Regimen IS NULL THEN 0
        ELSE 1
    END;


    ---------------------------------------
    -- 6. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE FosygaAuxiliar
	SET FechaConsulta = GETDATE()

    ------------------------------
    -- 7. Insertar en Fosyga
    -----------------------------
    INSERT INTO Fosyga
    SELECT fa.TitularID, fa.Estado, fa.EPS, fa.Regimen, fa.FechaAfiliacionEfectiva, fa.FechaFinalizacionAfiliacion, fa.TipoAfiliado, fa.Departamento, fa.Municipio, fa.EstadoEnFosyga, fa.FechaConsulta
    FROM FosygaAuxiliar fa;

    	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' INSERTADOS EN LA TABLA FOSYGA';

	-------------------------------------------
	-- 8. Eliminar duplicados en Fosyga
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, Estado, EPS, Regimen, TipoAfiliado ORDER BY FechaConsulta DESC) AS rn
        FROM Fosyga
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS DE FOSYGA';
	
END;

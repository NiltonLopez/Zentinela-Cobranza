CREATE PROCEDURE [dbo].[sp_ProcesarPositivaEmpresasAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;



      ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT fa.TitularID, fa.Nombre
    FROM PositivaEmpresasAuxiliar fa
    LEFT JOIN Titular t ON fa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT fa.TitularID, fa.CarteraID, fa.LiderID
    FROM PositivaEmpresasAuxiliar fa
    LEFT JOIN Titular_Cartera tc 
        ON fa.TitularID = tc.TitularID AND fa.CarteraID = tc.CarteraID AND fa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR CARTERA EN LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en PositivaEmpresaAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM PositivaEmpresasAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	 ---------------------------------------
    -- 4. Update el campo EstadoEnPositivaEmpresa en PositivaEmpresaAuxiliar
    ----------------------------------------

	UPDATE PositivaEmpresasAuxiliar
    SET EstadoEnPositivaEmpresa = CASE 
        WHEN NIT IS NULL AND Empresa IS NULL THEN 0
        ELSE 1
    END;

	----------------------------------------
    -- 5. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE PositivaEmpresasAuxiliar
	SET FechaConsulta = GETDATE()

    ------------------------------
    -- 6. Insertar en PositivaEmpresa
    -----------------------------
    INSERT INTO PositivaEmpresas
    SELECT ca.TitularID, ca.NIT, ca.Empresa, ca.EstadoAfiliacion , ca.EstadoEnPositivaEmpresa, ca.FechaConsulta
    FROM PositivaEmpresasAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' EMPRESAS FUERON INGRESADAS A POSITIVA DE EMPRESAS';

	-------------------------------------------
	-- 7. Eliminar duplicados en PositivaEmpresa
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NIT, Empresa ORDER BY FechaConsulta DESC) AS rn
        FROM PositivaEmpresas
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL DE EMPRESAS';

	
END;

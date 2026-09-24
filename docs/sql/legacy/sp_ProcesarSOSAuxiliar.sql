CREATE PROCEDURE [dbo].[sp_ProcesarSOSAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;



	 ------------------------------
    -- 1. Update a los campos en blanco
    ------------------------------

	UPDATE SOSAuxiliar SET Telefono = NULL WHERE Telefono = ''
	UPDATE SOSAuxiliar SET NitEmpresa = NULL WHERE NitEmpresa = 0
	UPDATE SOSAuxiliar SET RazonSocial = NULL WHERE RazonSocial = ''
	UPDATE SOSAuxiliar SET Dirección = NULL WHERE Dirección = ''
	UPDATE SOSAuxiliar SET EstadoEnSos = NULL WHERE EstadoEnSos = ''

    ------------------------------
    -- 2. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT sa.TitularID, sa.Nombre
    FROM SOSAuxiliar sa
    LEFT JOIN Titular t ON sa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 3. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID,LiderID)
    SELECT DISTINCT sa.TitularID, sa.CarteraID,sa.LiderID
	FROM SOSAuxiliar sa
    LEFT JOIN Titular_Cartera tc 
        ON sa.TitularID = tc.TitularID AND sa.CarteraID = tc.CarteraID AND sa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';
    ------------------------------
    -- 4. Eliminar duplicados en SOSAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM SOSAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;



	 ---------------------------------------
    -- 5. Update el campo EstadoEnSOS en SOSAuxiliar
    ----------------------------------------

	UPDATE SOSAuxiliar
    SET EstadoEnSOS = CASE 
        WHEN NitEmpresa IS NULL AND RazonSocial IS NULL AND Telefono IS NULL AND Dirección IS NULL  THEN 0
        ELSE 1
    END;

	----------------------------------------------------
	-- 6. Update el campo FechaConsulta en SOSAuxiliar
    ----------------------------------------------------

	UPDATE SOSAuxiliar
    SET FechaConsulta = GETDATE();


    -------------------------------
    -- 7. Insertar en SOS  
    -------------------------------
    INSERT INTO SOS
    SELECT sa.TitularID, sa.NitEmpresa, sa.RazonSocial, sa.Telefono, sa.Dirección, sa.EstadoEnSOS, sa.FechaConsulta
    FROM SOSAuxiliar sa;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A SOS';


	-------------------------------------------
	-- 8. Eliminar duplicados en SOS
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NitEmpresa, RazonSocial, Telefono,Direccion ORDER BY FechaConsulta DESC) AS rn
        FROM SOS
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';
	
END;

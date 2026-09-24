CREATE PROCEDURE [dbo].[sp_ProcesarSaludTotalAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;



	 ------------------------------
    -- 1. Update a los campos en blanco
    ------------------------------

	UPDATE SaludTotalAuxiliar SET NumeroTelefono = NULL WHERE NumeroTelefono = ''
    UPDATE SaludTotalAuxiliar SET Email = NULL WHERE Email = ''
    UPDATE SaludTotalAuxiliar SET FechaNacimiento = NULL WHERE FechaNacimiento = ''
    UPDATE SaludTotalAuxiliar SET EstadoAfiliacion = NULL WHERE EstadoAfiliacion = ''
    UPDATE SaludTotalAuxiliar SET TipoAfiliado = NULL WHERE TipoAfiliado = ''
    UPDATE SaludTotalAuxiliar SET Ciudad = NULL WHERE Ciudad = ''
	UPDATE SaludTotalAuxiliar SET NIT = NULL WHERE NIT = 0
	UPDATE SaludTotalAuxiliar SET Empresa = NULL WHERE Empresa = ''
	UPDATE SaludTotalAuxiliar SET Cargo = NULL WHERE Cargo = ''
	UPDATE SaludTotalAuxiliar SET IngresoBase = NULL WHERE IngresoBase = ''
	UPDATE SaludTotalAuxiliar SET UltimaFechaPago = NULL WHERE UltimaFechaPago = '1900-01-01 00:00:00.000' 

    ------------------------------
    -- 2. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT sa.TitularID, sa.Nombre
    FROM SaludTotalAuxiliar sa
    LEFT JOIN Titular t ON sa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 3. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID,LiderID)
    SELECT DISTINCT sa.TitularID, sa.CarteraID, sa.LiderID
	FROM SaludTotalAuxiliar sa
    LEFT JOIN Titular_Cartera tc 
        ON sa.TitularID = tc.TitularID AND sa.CarteraID = tc.CarteraID AND sa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';
    ------------------------------
    -- 4. Eliminar duplicados en SaludTotalAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM SaludTotalAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;



	 ---------------------------------------
    -- 5. Update el campo EstadoEnSaludTotal en SaludTotalAuxiliar
    ----------------------------------------

	UPDATE SaludTotalAuxiliar
    SET EstadoEnSaludTotal = CASE 
        WHEN NumeroTelefono IS NULL AND Email IS NULL AND NIT IS NULL THEN 0
        ELSE 1
    END;

	----------------------------------------------------
	-- 6. Update el campo FechaConsulta en RUESAuxiliar
    ----------------------------------------------------

	UPDATE SaludTotalAuxiliar
    SET FechaConsulta = GETDATE();


    -------------------------------
    -- 7. Insertar en SaludTotal  
    -------------------------------
    INSERT INTO SaludTotal
    SELECT sa.TitularID, sa.NumeroTelefono, sa.Email,sa.FechaNacimiento, sa.EstadoAfiliacion, sa.TipoAfiliado,
    sa.Ciudad, sa.NIT, sa.Empresa, sa.Cargo, sa.UltimaFechaPago, sa.IngresoBase, sa.EstadoEnSaludTotal, sa.FechaConsulta
    FROM SaludTotalAuxiliar sa;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A SALUD TOTAL';


	-------------------------------------------
	-- 8. Eliminar duplicados en SaludTotal
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NumeroTelefono, Email, NIT ORDER BY FechaConsulta DESC) AS rn
        FROM SaludTotal
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';
	
END;

CREATE PROCEDURE [dbo].[sp_ProcesarCamaraAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM CamaraAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM CamaraAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID AND ca.CarteraID = tc.CarteraID AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en CamaraAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM CamaraAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	 ---------------------------------------
    -- 4. Update el campo EstadoEnCamara en CamaraAuxiliar
    ----------------------------------------

	UPDATE CamaraAuxiliar
    SET EstadoEnCamara = CASE 
        WHEN NumeroTelefono IS NULL AND Email IS NULL THEN 0
        ELSE 1
    END;

	---------------------------------------
    -- 5. Update el campo Telefono y asignarle el prefijo en CamaraAuxiliar
    ---------------------------------------
	UPDATE CA
    SET CA.NumeroTelefono = CAST(P.Prefijo AS varchar) + CA.NumeroTelefono
    FROM CamaraAuxiliar CA
    INNER JOIN PrefijosDepartamento P
        ON UPPER(CA.Departamento) = P.Departamento
    WHERE LEN(CA.NumeroTelefono) = 7;


	---------------------------------------
    -- 6. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE CamaraAuxiliar
	SET FechaConsulta = GETDATE()


    -----------------------------
    -- 7. Insertar en Camara
    -----------------------------
    INSERT INTO Camara
    SELECT ca.TitularID, ca.NumeroTelefono, ca.Email, ca.Municipio, ca.Departamento, ca.EstadoEnCamara, ca.FechaConsulta
    FROM CamaraAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A CÁMARA';

	-------------------------------------------
	-- 8. Eliminar duplicados en Camara
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NumeroTelefono, Email ORDER BY FechaConsulta DESC) AS rn
        FROM Camara
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';

	
END;

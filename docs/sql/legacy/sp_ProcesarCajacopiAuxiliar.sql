CREATE PROCEDURE [dbo].[sp_ProcesarCajacopiAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM CajacopiAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM CajacopiAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID AND ca.CarteraID = tc.CarteraID AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en CajacopiAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM CajacopiAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	 ---------------------------------------
    -- 4. Update el campo EstadoEnCajacopi en CajacopiAuxiliar
    ----------------------------------------

	UPDATE CajacopiAuxiliar
    SET EstadoEnCajacopi = CASE 
        WHEN NumeroTelefonoCelular IS NULL AND NumeroTelefonoFijo IS NULL AND FechaNacimiento IS NULL AND Email IS NULL THEN 0
        ELSE 1
    END;

	---------------------------------------
    -- 5. Update el campo Telefono y asignarle el prefijo en CajacopiAuxiliar
    ---------------------------------------
	UPDATE CA
    SET CA.NumeroTelefonoFijo = 
        CASE 
            WHEN P2.Departamento IS NOT NULL THEN CAST(P2.Prefijo AS VARCHAR) + CA.NumeroTelefonoFijo
            WHEN P1.Municipio IS NOT NULL THEN CAST(P1.Prefijo AS VARCHAR) + CA.NumeroTelefonoFijo
            ELSE CA.NumeroTelefonoFijo
        END
    FROM CajacopiAuxiliar CA
    LEFT JOIN PrefijoMunicipio P1
        ON UPPER(CA.Municipio) = UPPER(P1.Municipio)
    LEFT JOIN PrefijoMunicipio P2
        ON UPPER(CA.Departamento) = UPPER(P2.Departamento)
    WHERE LEN(CA.NumeroTelefonoFijo) = 7;

	---------------------------------------
    -- 6. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE CajacopiAuxiliar
	SET FechaConsulta = GETDATE()


    -----------------------------
    -- 7. Insertar en Cajacopi
    -----------------------------
    INSERT INTO Cajacopi
    SELECT ca.TitularID, ca.NumeroTelefonoCelular, ca.NumeroTelefonoFijo, ca.Email, ca.Departamento, ca.Municipio, ca.FechaNacimiento,ca.EstadoEnCajacopi, ca.FechaConsulta
    FROM CajacopiAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A CAJACOPI';

	-------------------------------------------
	-- 8. Eliminar duplicados en Cajacopi
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NumeroTelefonoCelular,NumeroTelefonoFijo, Email ORDER BY FechaConsulta DESC) AS rn
        FROM Cajacopi
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';


    -------------------------------------------
	-- 9. Eliminar Numeros Y Correos Errados en Cajacopi
    -------------------------------------------
    UPDATE Cajacopi
    SET NumeroTelefonoFijo = NULL
    WHERE NumeroTelefonoFijo LIKE '%11111%';

    UPDATE Cajacopi
    SET NumeroTelefonoFijo = NULL
    WHERE NumeroTelefonoFijo NOT LIKE '6%';

    UPDATE Cajacopi
    SET NumeroTelefonoCelular = NULL
    WHERE NumeroTelefonoCelular NOT LIKE '3%';

	UPDATE Cajacopi
    SET Email = NULL
    WHERE Email LIKE '%NOTIEN%';
	
END;

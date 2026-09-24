CREATE PROCEDURE [dbo].[sp_ProcesarPositivaAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM PositivaAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

	--------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM PositivaAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID AND ca.CarteraID = tc.CarteraID AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en PositivaAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM PositivaAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

     ------------------------------
    -- 4. Organizar Telefonos Fijos
    ------------------------------
    UPDATE PositivaAuxiliar
    SET 
        NumeroTelefonoFijo = 
            CASE 
                WHEN NumeroTelefonoFijo IS NULL OR NumeroTelefonoFijo = '' THEN NumeroTelefonoCelular
                ELSE NumeroTelefonoFijo + ', ' + NumeroTelefonoCelular
            END,
        NumeroTelefonoCelular = NULL
    WHERE 
        NumeroTelefonoCelular LIKE '6%';

    ------------------------------
    -- 5. Limpiar correos con base en ListaNegra
    ------------------------------
    UPDATE PositivaAuxiliar
    SET Email = ''
    WHERE EXISTS (
        SELECT 1 
        FROM ListaNegraCorreos LNC
        WHERE PositivaAuxiliar.Email LIKE '%' + LNC.CorreoErroneo + '%'
    );

	 ---------------------------------------
    -- 6. Update el campo EstadoEnPositiva en PositivaAuxiliar
    ----------------------------------------

	UPDATE PositivaAuxiliar
    SET EstadoEnPositiva = CASE 
        WHEN NumeroTelefonoCelular IS NULL AND Email IS NULL AND NumeroTelefonoFijo IS NULL THEN 0
        ELSE 1
    END;

	---------------------------------------
    -- 7. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE PositivaAuxiliar
	SET FechaConsulta = GETDATE()


    -----------------------------
    -- 8. Insertar en Positiva
    -----------------------------
    INSERT INTO Positiva
    SELECT ca.TitularID, ca.NumeroTelefonoCelular,ca.NumeroTelefonoFijo , ca.Email, ca.FechaNacimiento , ca.Departamento , ca.Municipio, ca.EstadoEnPositiva, ca.FechaConsulta
    FROM PositivaAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A POSITIVA';

	-------------------------------------------
	-- 9. Eliminar duplicados en Positiva
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID, NumeroTelefonoCelular , NumeroTelefonoFijo , Email ORDER BY FechaConsulta DESC) AS rn
        FROM Positiva
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';


	-------------------------------------------
	-- 10. Eliminar registro nulo que tenga datos en Positiva
    -------------------------------------------
	DELETE FROM Positiva
	WHERE EstadoEnPositiva = 0
	AND TitularID IN (
	    SELECT TitularID
	    FROM Positiva
	    GROUP BY TitularID
	    HAVING 
	        SUM(CASE WHEN (NumeroTelefonoCelular IS NULL AND NumeroTelefonoFijo IS NULL AND Email IS NULL) THEN 1 ELSE 0 END) >= 1 AND
	        SUM(CASE WHEN (NumeroTelefonoCelular IS NOT NULL AND NumeroTelefonoFijo IS NOT NULL AND Email IS  NOT NULL) THEN 1 ELSE 0 END) >= 1
	);
	
END;

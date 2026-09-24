CREATE PROCEDURE [dbo].[sp_ProcesarCamaraEmpresaAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;


    ------------------------------
    -- 1. Eliminar duplicados en CamaraEmpresaAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY NitEmpresa ORDER BY FechaConsulta DESC) AS rn
        FROM CamaraEmpresaAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	 ---------------------------------------
    -- 2. Update el campo EstadoEnCamaraEmpresa en CamaraEmpresaAuxiliar
    ----------------------------------------

	UPDATE CamaraEmpresaAuxiliar
    SET EstadoEnCamara = CASE 
        WHEN NumeroTelefono IS NULL AND Email IS NULL THEN 0
        ELSE 1
    END;

	 ---------------------------------------
    -- 3. Update el campo Telefono y asignarle el prefijo en CamaraEmpresaAuxiliar
    ----------------------------------------
	UPDATE CA
    SET CA.NumeroTelefono = CAST(P.Prefijo AS varchar) + CA.NumeroTelefono
    FROM CamaraEmpresaAuxiliar CA
    INNER JOIN PrefijosDepartamento P
        ON UPPER(CA.Departamento) = P.Departamento
    WHERE LEN(CA.NumeroTelefono) = 7;


		 ---------------------------------------
    -- 4. Update el FechaConsulta y asignarle fecha del dia actual
    ----------------------------------------
	UPDATE CamaraEmpresaAuxiliar
	SET FechaConsulta = GETDATE()

    ------------------------------
    -- 5. Insertar en CamaraEmpresa
    -----------------------------
    INSERT INTO CamaraEmpresa
    SELECT ca.NitEmpresa, ca.NumeroTelefono, ca.Email, ca.EstadoEnCamara, ca.FechaConsulta
    FROM CamaraEmpresaAuxiliar ca;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' EMPRESAS FUERON INGRESADAS A CÁMARA DE EMPRESAS';

	-------------------------------------------
	-- 6. Eliminar duplicados en CamaraEmpresa
    ---------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY NitEmpresa, NumeroTelefono, Email ORDER BY FechaConsulta DESC) AS rn
        FROM CamaraEmpresa
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

	PRINT CAST (@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL DE EMPRESAS';

	
END;

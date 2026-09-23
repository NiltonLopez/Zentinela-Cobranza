CREATE PROCEDURE [dbo].[sp_ProcesarSIMITAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT sa.TitularID, sa.Nombre
    FROM SIMITAuxiliar sa
    LEFT JOIN Titular t ON sa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';


    --------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT sa.TitularID, sa.CarteraID, sa.LiderID
    FROM SIMITAuxiliar sa
    LEFT JOIN Titular_Cartera tc 
        ON sa.TitularID = tc.TitularID 
        AND sa.CarteraID = tc.CarteraID 
        AND sa.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA';


    ------------------------------
    -- 3. Eliminar duplicados en SIMITAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM SIMITAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;


    ---------------------------------------
    -- 4. Actualizar EstadoEnSIMIT
    ---------------------------------------
    UPDATE SIMITAuxiliar
    SET EstadoEnSIMIT = CASE 
        WHEN Placa1 IS NULL AND Placa2 IS NULL AND Placa3 IS NULL 
         AND Placa4 IS NULL AND Placa5 IS NULL AND Placa6 IS NULL
         AND Placa7 IS NULL AND Placa8 IS NULL AND Placa9 IS NULL
         AND Placa10 IS NULL AND Placa11 IS NULL AND Placa12 IS NULL
         AND Placa13 IS NULL AND Placa14 IS NULL AND Placa15 IS NULL
        THEN 0
        ELSE 1
    END;


    ---------------------------------------
    -- 5. Actualizar FechaConsulta
    ---------------------------------------
    UPDATE SIMITAuxiliar
    SET FechaConsulta = GETDATE();


    ---------------------------------------
    -- 6. Insertar en SIMIT (NORMALIZANDO PLACAS)
    ---------------------------------------
    INSERT INTO SIMIT (TitularID, Placa, EstadoEnSIMIT, FechaConsulta)
    SELECT DISTINCT
    sa.TitularID,
    placas.Placa,
    sa.EstadoEnSIMIT,
    sa.FechaConsulta
    FROM SIMITAuxiliar sa
    OUTER APPLY (
    VALUES 
    (Placa1),(Placa2),(Placa3),(Placa4),(Placa5),
    (Placa6),(Placa7),(Placa8),(Placa9),(Placa10),
    (Placa11),(Placa12),(Placa13),(Placa14),(Placa15)
    ) AS placas(Placa)
    WHERE 
    placas.Placa IS NOT NULL
    OR sa.EstadoEnSIMIT = 0;


    -------------------------------------------
    -- 7. Eliminar duplicados en SIMIT
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (
                PARTITION BY TitularID, Placa 
                ORDER BY FechaConsulta DESC
            ) AS rn
        FROM SIMIT
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS ELIMINADOS EN SIMIT';

END;

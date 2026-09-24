CREATE PROCEDURE [dbo].[sp_ProcesarMiVacunaAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT mv.TitularID, mv.Nombre
    FROM MiVacunaAuxiliar mv
    LEFT JOIN Titular t ON mv.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';


    --------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT mv.TitularID, mv.CarteraID, mv.LiderID
    FROM MiVacunaAuxiliar mv
    INNER JOIN Lider_Cartera lc 
        ON mv.LiderID = lc.LiderID
    LEFT JOIN Titular_Cartera tc 
        ON mv.TitularID = tc.TitularID 
        AND mv.CarteraID = tc.CarteraID 
        AND mv.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA';


    ------------------------------
    -- 3. Eliminar duplicados en MiVacunaAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM MiVacunaAuxiliar
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;


    ------------------------------
    -- 4. Limpiar correos con Lista Negra
    ------------------------------
    UPDATE mv
    SET Email = ''
    FROM MiVacunaAuxiliar mv
    WHERE EXISTS (
        SELECT 1 
        FROM ListaNegraCorreos lnc
        WHERE mv.Email LIKE '%' + lnc.CorreoErroneo + '%'
    );


    ------------------------------
    -- 5. Normalizar celular
    ------------------------------
    UPDATE MiVacunaAuxiliar
    SET Celular = NULL
    WHERE Celular = '' OR Celular = '0';


    ---------------------------------------
    -- 6. Update EstadoEnMiVacuna
    ---------------------------------------
    UPDATE MiVacunaAuxiliar
    SET EstadoEnMiVacuna = CASE 
        WHEN Celular IS NULL AND Email IS NULL THEN 0
        ELSE 1
    END;

    ---------------------------------------
    -- 7. Update FechaConsulta
    ---------------------------------------
    UPDATE MiVacunaAuxiliar
    SET FechaConsulta = GETDATE();


    -----------------------------
    -- 8. Insertar en MiVacuna
    -----------------------------
    INSERT INTO MiVacuna (
        TitularID,
        Celular,
        Email,
        FechaExpedicion,
        EstadoEnMiVacuna,
        FechaConsulta
    )
    SELECT 
        mv.TitularID,
        mv.Celular,
        mv.Email,
        mv.FechaExpedicion,
        mv.EstadoEnMiVacuna,
        mv.FechaConsulta
    FROM MiVacunaAuxiliar mv;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A MIVACUNA';


    -------------------------------------------
    -- 9. Eliminar duplicados en MiVacuna
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (
                PARTITION BY TitularID, Celular, Email 
                ORDER BY FechaConsulta DESC
            ) AS rn
        FROM MiVacuna
    )
    DELETE FROM CTE_Duplicados WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS ELIMINADOS EN MIVACUNA';


    -------------------------------------------
    -- 10. Eliminar registros vacíos si ya hay datos buenos
    -------------------------------------------
    DELETE FROM MiVacuna
    WHERE EstadoEnMiVacuna = 0
    AND TitularID IN (
        SELECT TitularID
        FROM MiVacuna
        GROUP BY TitularID
        HAVING 
            SUM(CASE WHEN (Celular IS NULL AND Email IS NULL) THEN 1 ELSE 0 END) >= 1
            AND
            SUM(CASE WHEN (Celular IS NOT NULL OR Email IS NOT NULL) THEN 1 ELSE 0 END) >= 1
    );

END;

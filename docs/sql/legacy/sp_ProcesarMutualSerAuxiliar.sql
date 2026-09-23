CREATE PROCEDURE [dbo].[sp_ProcesarMutualSerAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT ca.TitularID, ca.Nombre
    FROM MutualSerAuxiliar ca
    LEFT JOIN Titular t ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A LA BASE DE DATOS';

    --------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT ca.TitularID, ca.CarteraID, ca.LiderID
    FROM MutualSerAuxiliar ca
    LEFT JOIN Titular_Cartera tc 
        ON ca.TitularID = tc.TitularID 
        AND ca.CarteraID = tc.CarteraID 
        AND ca.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA LA BASE DE DATOS';

    ------------------------------
    -- 3. Eliminar duplicados en MutualSerAuxiliar
    ------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn
        FROM MutualSerAuxiliar
    )
    DELETE FROM CTE_Duplicados 
    WHERE rn > 1;

    -------------------------------------------------
    -- 4. Si Celular y Celular2 son iguales, limpiar Celular2
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET NumeroTelefonoCelular2 = NULL
    WHERE 
        NumeroTelefonoCelular IS NOT NULL
        AND NumeroTelefonoCelular2 IS NOT NULL
        AND LTRIM(RTRIM(NumeroTelefonoCelular)) = LTRIM(RTRIM(NumeroTelefonoCelular2));

    -------------------------------------------------
    -- 5. Si el Fijo está repetido en Celular o Celular2, limpiar Fijo
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET NumeroTelefonoFijo = NULL
    WHERE 
        NumeroTelefonoFijo IS NOT NULL
        AND (
            LTRIM(RTRIM(NumeroTelefonoFijo)) = LTRIM(RTRIM(NumeroTelefonoCelular))
            OR LTRIM(RTRIM(NumeroTelefonoFijo)) = LTRIM(RTRIM(NumeroTelefonoCelular2))
        );

    -------------------------------------------------
    -- 6. Si el Fijo realmente es un celular, moverlo a Celular2
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET 
        NumeroTelefonoCelular2 = NumeroTelefonoFijo,
        NumeroTelefonoFijo = NULL
    WHERE 
        NumeroTelefonoFijo IS NOT NULL
        AND NumeroTelefonoFijo LIKE '3%'
        AND LEN(LTRIM(RTRIM(NumeroTelefonoFijo))) = 10
        AND (
            NumeroTelefonoCelular2 IS NULL 
            OR NumeroTelefonoCelular2 = ''
        )
        AND LTRIM(RTRIM(NumeroTelefonoFijo)) <> ISNULL(LTRIM(RTRIM(NumeroTelefonoCelular)), '');

    -------------------------------------------------
    -- 7. Eliminar fijos cuyo largo sea diferente a 10
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET NumeroTelefonoFijo = NULL
    WHERE 
        NumeroTelefonoFijo IS NOT NULL
        AND NumeroTelefonoFijo <> ''
        AND LEN(LTRIM(RTRIM(NumeroTelefonoFijo))) <> 10;

    -------------------------------------------------
    -- 8. Si hay un fijo en NumeroTelefonoCelular, moverlo a NumeroTelefonoFijo
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET 
        NumeroTelefonoFijo = CASE
            WHEN NumeroTelefonoFijo IS NULL OR NumeroTelefonoFijo = ''
                THEN NumeroTelefonoCelular
            ELSE NumeroTelefonoFijo + ', ' + NumeroTelefonoCelular
        END,
        NumeroTelefonoCelular = NULL
    WHERE
        NumeroTelefonoCelular IS NOT NULL
        AND NumeroTelefonoCelular <> ''
        AND LEN(LTRIM(RTRIM(NumeroTelefonoCelular))) = 10
        AND NumeroTelefonoCelular LIKE '60%';

    -------------------------------------------------
    -- 9. Si hay un fijo en NumeroTelefonoCelular2, moverlo a NumeroTelefonoFijo
    -------------------------------------------------
    UPDATE MutualSerAuxiliar
    SET 
        NumeroTelefonoFijo = CASE
            WHEN NumeroTelefonoFijo IS NULL OR NumeroTelefonoFijo = ''
                THEN NumeroTelefonoCelular2
            ELSE NumeroTelefonoFijo + ', ' + NumeroTelefonoCelular2
        END,
        NumeroTelefonoCelular2 = NULL
    WHERE
        NumeroTelefonoCelular2 IS NOT NULL
        AND NumeroTelefonoCelular2 <> ''
        AND LEN(LTRIM(RTRIM(NumeroTelefonoCelular2))) = 10
        AND NumeroTelefonoCelular2 LIKE '60%';

    ------------------------------
    -- 10. Limpiar correos con base en ListaNegra
    ------------------------------
    UPDATE MutualSerAuxiliar
    SET Email = ''
    WHERE EXISTS (
        SELECT 1 
        FROM ListaNegraCorreos LNC
        WHERE MutualSerAuxiliar.Email LIKE '%' + LNC.CorreoErroneo + '%'
    );

    ---------------------------------------
    -- 11. Actualizar EstadoEnMutualSer
    ---------------------------------------
    UPDATE MutualSerAuxiliar
    SET EstadoEnMutualSer = CASE 
        WHEN 
            (NumeroTelefonoCelular IS NULL OR NumeroTelefonoCelular = '')
            AND (NumeroTelefonoCelular2 IS NULL OR NumeroTelefonoCelular2 = '')
            AND (NumeroTelefonoFijo IS NULL OR NumeroTelefonoFijo = '')
            AND (Email IS NULL OR Email = '')
        THEN 0
        ELSE 1
    END;

    ---------------------------------------
    -- 12. Actualizar FechaConsulta
    ---------------------------------------
    UPDATE MutualSerAuxiliar
    SET FechaConsulta = GETDATE();

    -----------------------------
    -- 13. Insertar en MutualSer
    -----------------------------
    INSERT INTO MutualSer (
        TitularID,
        NumeroTelefonoCelular,
        NumeroTelefonoCelular2,
        NumeroTelefonoFijo,
        Email,
        Regimen,
        TipoAfiliado,
        Departamento,
        Municipio,
        EstadoEnMutualSer,
        FechaConsulta
    )
    SELECT 
        ca.TitularID,
        ca.NumeroTelefonoCelular,
        ca.NumeroTelefonoCelular2,
        ca.NumeroTelefonoFijo,
        ca.Email,
        ca.Regimen,
        ca.TipoAfiliado,
        ca.Departamento,
        ca.Municipio,
        ca.EstadoEnMutualSer,
        ca.FechaConsulta
    FROM MutualSerAuxiliar ca;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' PERSONAS FUERON INGRESADAS A MUTUALSER';

    -------------------------------------------
    -- 14. Eliminar duplicados en MutualSer
    -------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (
                PARTITION BY 
                    TitularID, 
                    NumeroTelefonoCelular, 
                    NumeroTelefonoCelular2, 
                    NumeroTelefonoFijo, 
                    Email 
                ORDER BY FechaConsulta DESC
            ) AS rn
        FROM MutualSer
    )
    DELETE FROM CTE_Duplicados 
    WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS FUERON ELIMINADOS EN LA TABLA PRINCIPAL';

    -------------------------------------------
    -- 15. Eliminar registro nulo que tenga datos en MutualSer
    -------------------------------------------
    DELETE FROM MutualSer
    WHERE EstadoEnMutualSer = 0
    AND TitularID IN (
        SELECT TitularID
        FROM MutualSer
        GROUP BY TitularID
        HAVING 
            SUM(CASE 
                WHEN 
                    (NumeroTelefonoCelular IS NULL OR NumeroTelefonoCelular = '')
                    AND (NumeroTelefonoCelular2 IS NULL OR NumeroTelefonoCelular2 = '')
                    AND (NumeroTelefonoFijo IS NULL OR NumeroTelefonoFijo = '')
                    AND (Email IS NULL OR Email = '')
                THEN 1 ELSE 0 
            END) >= 1
            AND
            SUM(CASE 
                WHEN 
                    (NumeroTelefonoCelular IS NOT NULL AND NumeroTelefonoCelular <> '')
                    OR (NumeroTelefonoCelular2 IS NOT NULL AND NumeroTelefonoCelular2 <> '')
                    OR (NumeroTelefonoFijo IS NOT NULL AND NumeroTelefonoFijo <> '')
                    OR (Email IS NOT NULL AND Email <> '')
                THEN 1 ELSE 0 
            END) >= 1
    );

END;

CREATE PROCEDURE [dbo].[sp_ProcesarSuraAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    -------------------------------------------------
    -- 1. Insertar en Titular si no existe
    -------------------------------------------------
    INSERT INTO Titular (
        NumeroDocumento,
        Nombre
    )
    SELECT DISTINCT
        sa.TitularID,
        sa.Nombre
    FROM SuraAuxiliar sa
    LEFT JOIN Titular t
        ON sa.TitularID = t.NumeroDocumento
    WHERE
        t.NumeroDocumento IS NULL
        AND sa.TitularID IS NOT NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' PERSONAS FUERON INGRESADAS A LA TABLA TITULAR';


    -------------------------------------------------
    -- 2. Insertar relación en Titular_Cartera
    -------------------------------------------------
    INSERT INTO Titular_Cartera (
        TitularID,
        CarteraID,
        LiderID
    )
    SELECT DISTINCT
        sa.TitularID,
        sa.CarteraID,
        sa.LiderID
    FROM SuraAuxiliar sa
    LEFT JOIN Titular_Cartera tc
        ON sa.TitularID = tc.TitularID
        AND sa.CarteraID = tc.CarteraID
        AND sa.LiderID = tc.LiderID
    WHERE
        tc.TitularID IS NULL
        AND sa.TitularID IS NOT NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA';


    -------------------------------------------------
    -- 3. Eliminar duplicados en SuraAuxiliar
    --    Conserva el registro más reciente
    -------------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (
                PARTITION BY TitularID
                ORDER BY FechaConsulta DESC
            ) AS rn
        FROM SuraAuxiliar
    )
    DELETE FROM CTE_Duplicados
    WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' DUPLICADOS FUERON ELIMINADOS DE SURA AUXILIAR';


    -------------------------------------------------
    -- 4. Limpiar celulares vacíos
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET NumeroCelular = NULL
    WHERE
        LTRIM(RTRIM(ISNULL(NumeroCelular, ''))) = '';


    -------------------------------------------------
    -- 5. Eliminar celulares que no tengan
    --    exactamente 10 dígitos
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET NumeroCelular = NULL
    WHERE
        NumeroCelular IS NOT NULL
        AND LEN(LTRIM(RTRIM(NumeroCelular))) <> 10;


    -------------------------------------------------
    -- 6. Eliminar números que no sean celulares
    --    colombianos
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET NumeroCelular = NULL
    WHERE
        NumeroCelular IS NOT NULL
        AND NumeroCelular NOT LIKE '3%';


    -------------------------------------------------
    -- 7. Limpiar correos vacíos
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET Email = NULL
    WHERE
        LTRIM(RTRIM(ISNULL(Email, ''))) = '';


    -------------------------------------------------
    -- 8. Limpiar correos incluidos en ListaNegraCorreos
    -------------------------------------------------
    UPDATE sa
    SET sa.Email = NULL
    FROM SuraAuxiliar sa
    WHERE EXISTS (
        SELECT 1
        FROM ListaNegraCorreos lnc
        WHERE sa.Email LIKE '%' + lnc.CorreoErroneo + '%'
    );


    -------------------------------------------------
    -- 9. Actualizar EstadoEnSura
    --
    -- 0 = No se encontró ninguna información útil
    -- 1 = Existe al menos algún dato proveniente de Sura
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET EstadoEnSura =
        CASE
            WHEN
                NumeroCelular IS NULL
                AND (Email IS NULL OR Email = '')
                AND NIT IS NULL
                AND (Empresa IS NULL OR Empresa = '')
                AND (TipoAfiliado IS NULL OR TipoAfiliado = '')
                AND (EstadoAfiliacion IS NULL OR EstadoAfiliacion = '')
            THEN 0
            ELSE 1
        END;


    -------------------------------------------------
    -- 10. Actualizar FechaConsulta
    -------------------------------------------------
    UPDATE SuraAuxiliar
    SET FechaConsulta = GETDATE();


    -------------------------------------------------
    -- 11. Insertar información en Sura
    -------------------------------------------------
    INSERT INTO Sura (
        TitularID,
        NumeroCelular,
        Email,
        NIT,
        Empresa,
        TipoAfiliado,
        UltimaFechaConsulta,
        EstadoAfiliacion,
        EstadoEnSura,
        FechaConsulta
    )
    SELECT
        sa.TitularID,
        sa.NumeroCelular,
        sa.Email,
        sa.NIT,
        sa.Empresa,
        sa.TipoAfiliado,
        sa.UltimaFechaConsulta,
        sa.EstadoAfiliacion,
        sa.EstadoEnSura,
        sa.FechaConsulta
    FROM SuraAuxiliar sa
    WHERE sa.TitularID IS NOT NULL;

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' PERSONAS FUERON INGRESADAS A SURA';


    -------------------------------------------------
    -- 12. Eliminar duplicados exactos en Sura
    -------------------------------------------------
    WITH CTE_Duplicados AS (
        SELECT *,
            ROW_NUMBER() OVER (
                PARTITION BY
                    TitularID,
                    NumeroCelular,
                    Email,
                    NIT,
                    Empresa,
                    TipoAfiliado,
                    EstadoAfiliacion
                ORDER BY FechaConsulta DESC
            ) AS rn
        FROM Sura
    )
    DELETE FROM CTE_Duplicados
    WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' DUPLICADOS FUERON ELIMINADOS DE SURA';


    -------------------------------------------------
    -- 13. Eliminar registros vacíos si el titular
    --     también tiene un registro con información
    -------------------------------------------------
    DELETE FROM Sura
    WHERE EstadoEnSura = 0
    AND TitularID IN (
        SELECT TitularID
        FROM Sura
        GROUP BY TitularID
        HAVING
            SUM(
                CASE
                    WHEN EstadoEnSura = 0
                    THEN 1
                    ELSE 0
                END
            ) >= 1
            AND
            SUM(
                CASE
                    WHEN EstadoEnSura = 1
                    THEN 1
                    ELSE 0
                END
            ) >= 1
    );

    PRINT CAST(@@ROWCOUNT AS VARCHAR)
        + ' REGISTROS VACIOS FUERON ELIMINADOS DE SURA';

END;

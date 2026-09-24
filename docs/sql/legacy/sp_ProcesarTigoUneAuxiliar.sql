CREATE PROCEDURE [dbo].[sp_ProcesarTigoUneAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;
    -------------------------------------------------
    -- 1. Insertar en Titular si no existe
    -------------------------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT
        ta.TitularID,
        ta.Nombre
    FROM TigoUneAuxiliar ta
    LEFT JOIN Titular t
        ON ta.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS FUERON INGRESADAS EN TITULAR';
    -------------------------------------------------
    -- 2. Relacionar Titular con Cartera
    -------------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT
        ta.TitularID,
        ta.CarteraID,
        ta.LiderID
    FROM TigoUneAuxiliar ta
    LEFT JOIN Titular_Cartera tc
        ON tc.TitularID = ta.TitularID
       AND tc.CarteraID = ta.CarteraID
       AND tc.LiderID = ta.LiderID
    WHERE tc.TitularID IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS RELACIONADAS CON LA CARTERA';
    -------------------------------------------------
    -- 3. Eliminar duplicados en la tabla auxiliar
    -------------------------------------------------
    ;WITH Duplicados AS
    (
        SELECT *,
               ROW_NUMBER() OVER
               (
                    PARTITION BY TitularID
                    ORDER BY TitularID
               ) AS RN
        FROM TigoUneAuxiliar
    )
    DELETE FROM Duplicados
    WHERE RN > 1;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' DUPLICADOS ELIMINADOS EN LA TABLA AUXILIAR';
    -------------------------------------------------
    -- 4. Limpiar correos pertenecientes a la lista negra
    -------------------------------------------------
    UPDATE ta
       SET Email = NULL
    FROM TigoUneAuxiliar ta
    WHERE EXISTS
    (
        SELECT 1
        FROM ListaNegraCorreos l
        WHERE ta.Email LIKE '%' + l.CorreoErroneo + '%'
    );
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' CORREOS LIMPIADOS';
    -------------------------------------------------
    -- 5. Limpiar numeros con longitud incorrecta
    -------------------------------------------------
    UPDATE TigoUneAuxiliar
       SET NumeroTelefono = CASE WHEN LEN(ISNULL(NumeroTelefono,'')) <> 10 THEN NULL ELSE NumeroTelefono END;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' NUMEROS LIMPIADOS POR LONGITUD INCORRECTA';
    -------------------------------------------------
    -- 6. Actualizar EstadoEnTigoUne
    -------------------------------------------------
    UPDATE TigoUneAuxiliar
       SET EstadoEnTigoUne =
            CASE
                WHEN ISNULL(NumeroTelefono,'') = ''
                 AND ISNULL(Email,'') = ''
                THEN 0
                ELSE 1
            END;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' REGISTROS ACTUALIZADOS';
    -------------------------------------------------
    -- 7. Insertar unicamente registros nuevos
    -------------------------------------------------
    INSERT INTO TigoUne
    (
        TitularID,
        NumeroTelefono,
        Email,
        EstadoEnTigoUne,
        FechaConsulta
    )
    SELECT
        ta.TitularID,
        ta.NumeroTelefono,
        ta.Email,
        ta.EstadoEnTigoUne,
        CAST(GETDATE() AS DATE)
    FROM TigoUneAuxiliar ta
    LEFT JOIN TigoUne t
        ON t.TitularID = ta.TitularID
       AND ISNULL(t.NumeroTelefono,'') = ISNULL(ta.NumeroTelefono,'')
       AND ISNULL(t.Email,'') = ISNULL(ta.Email,'')
    WHERE t.TitularID IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS INGRESADAS EN TIGOUНЕ';
END;

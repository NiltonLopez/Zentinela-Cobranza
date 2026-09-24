CREATE PROCEDURE [dbo].[sp_ProcesarCoosaludAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;
    -------------------------------------------------
    -- 1. Insertar en Titular si no existe
    -------------------------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT
        ca.TitularID,
        ca.Nombre
    FROM CoosaludAuxiliar ca
    LEFT JOIN Titular t
        ON ca.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS FUERON INGRESADAS EN TITULAR';
    -------------------------------------------------
    -- 2. Relacionar Titular con Cartera
    -------------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT
        ca.TitularID,
        ca.CarteraID,
        ca.LiderID
    FROM CoosaludAuxiliar ca
    LEFT JOIN Titular_Cartera tc
        ON tc.TitularID = ca.TitularID
       AND tc.CarteraID = ca.CarteraID
       AND tc.LiderID = ca.LiderID
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
        FROM CoosaludAuxiliar
    )
    DELETE FROM Duplicados
    WHERE RN > 1;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' DUPLICADOS ELIMINADOS EN LA TABLA AUXILIAR';
    -------------------------------------------------
    -- 4. Limpiar correos pertenecientes a la lista negra
    -------------------------------------------------
    UPDATE ca
       SET Email = NULL
    FROM CoosaludAuxiliar ca
    WHERE EXISTS
    (
        SELECT 1
        FROM ListaNegraCorreos l
        WHERE ca.Email LIKE '%' + l.CorreoErroneo + '%'
    );
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' CORREOS LIMPIADOS';
    -------------------------------------------------
    -- 5. Limpiar numeros con longitud incorrecta
    -------------------------------------------------
    UPDATE CoosaludAuxiliar
       SET NumeroTelefono = CASE WHEN LEN(ISNULL(NumeroTelefono,'')) <> 10 THEN NULL ELSE NumeroTelefono END;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' NUMEROS LIMPIADOS POR LONGITUD INCORRECTA';
    -------------------------------------------------
    -- 6. Actualizar EstadoEnCoosalud
    -------------------------------------------------
    UPDATE CoosaludAuxiliar
       SET EstadoEnCoosalud =
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
    INSERT INTO Coosalud
    (
        TitularID,
        NumeroTelefono,
        Email,
        MunicipioOrigen,
        DepartamentoOrigen,
        Regimen,
        TipoAfiliado,
        EstadoAfiliacion,
        NivelSisben,
        FechaNacimiento,
        EstadoEnCoosalud,
        FechaConsulta
    )
    SELECT
        ca.TitularID,
        ca.NumeroTelefono,
        ca.Email,
        ca.MunicipioOrigen,
        ca.DepartamentoOrigen,
        ca.Regimen,
        ca.TipoAfiliado,
        ca.EstadoAfiliacion,
        ca.NivelSisben,
        CAST(ca.FechaNacimiento AS DATE),
        ca.EstadoEnCoosalud,
        CAST(GETDATE() AS DATE)
    FROM CoosaludAuxiliar ca
    LEFT JOIN Coosalud c
        ON c.TitularID = ca.TitularID
       AND ISNULL(c.NumeroTelefono,'') = ISNULL(ca.NumeroTelefono,'')
       AND ISNULL(c.Email,'') = ISNULL(ca.Email,'')
       AND ISNULL(c.TipoAfiliado,'') = ISNULL(ca.TipoAfiliado,'')
    WHERE c.TitularID IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS INGRESADAS EN COOSALUD';
END;

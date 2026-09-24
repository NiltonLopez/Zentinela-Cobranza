CREATE PROCEDURE [dbo].[sp_ProcesarSaviaSaludAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;
    -------------------------------------------------
    -- 1. Insertar en Titular si no existe
    -------------------------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT
        sa.TitularID,
        sa.Nombre
    FROM SaviaSaludAuxiliar sa
    LEFT JOIN Titular t
        ON sa.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS FUERON INGRESADAS EN TITULAR';
    -------------------------------------------------
    -- 2. Relacionar Titular con Cartera
    -------------------------------------------------
    INSERT INTO Titular_Cartera (TitularID, CarteraID, LiderID)
    SELECT DISTINCT
        sa.TitularID,
        sa.CarteraID,
        sa.LiderID
    FROM SaviaSaludAuxiliar sa
    LEFT JOIN Titular_Cartera tc
        ON tc.TitularID = sa.TitularID
       AND tc.CarteraID = sa.CarteraID
       AND tc.LiderID = sa.LiderID
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
        FROM SaviaSaludAuxiliar
    )
    DELETE FROM Duplicados
    WHERE RN > 1;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' DUPLICADOS ELIMINADOS EN LA TABLA AUXILIAR';
    -------------------------------------------------
    -- 4. Limpiar correos pertenecientes a la lista negra
    -------------------------------------------------
    UPDATE sa
       SET Email = NULL
    FROM SaviaSaludAuxiliar sa
    WHERE EXISTS
    (
        SELECT 1
        FROM ListaNegraCorreos l
        WHERE sa.Email LIKE '%' + l.CorreoErroneo + '%'
    );
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' CORREOS LIMPIADOS';
    -------------------------------------------------
    -- 5. Limpiar fragmentos invalidos dentro de los numeros
    -------------------------------------------------
    UPDATE SaviaSaludAuxiliar
       SET NumeroTelefono = (
            SELECT STRING_AGG(LTRIM(RTRIM(s.value)), ',')
            FROM STRING_SPLIT(NumeroTelefono, ',') s
            WHERE LEN(LTRIM(RTRIM(s.value))) = 10
       )
    WHERE NumeroTelefono IS NOT NULL;
    UPDATE SaviaSaludAuxiliar
       SET NumeroCelular = (
            SELECT STRING_AGG(LTRIM(RTRIM(s.value)), ',')
            FROM STRING_SPLIT(NumeroCelular, ',') s
            WHERE LEN(LTRIM(RTRIM(s.value))) = 10
       )
    WHERE NumeroCelular IS NOT NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' FRAGMENTOS INVALIDOS ELIMINADOS DE NUMEROS';
    -------------------------------------------------
    -- 6. Limpiar numeros con longitud incorrecta
    -------------------------------------------------
    UPDATE SaviaSaludAuxiliar
       SET NumeroTelefono = NULL
    WHERE NumeroTelefono IS NOT NULL
      AND NOT EXISTS (
            SELECT 1
            FROM STRING_SPLIT(NumeroTelefono, ',') s
            WHERE LEN(LTRIM(RTRIM(s.value))) = 10
      );
    UPDATE SaviaSaludAuxiliar
       SET NumeroCelular = NULL
    WHERE NumeroCelular IS NOT NULL
      AND NOT EXISTS (
            SELECT 1
            FROM STRING_SPLIT(NumeroCelular, ',') s
            WHERE LEN(LTRIM(RTRIM(s.value))) = 10
      );
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' NUMEROS LIMPIADOS POR LONGITUD INCORRECTA';
    -------------------------------------------------
    -- 7. Actualizar Estado
    -------------------------------------------------
    UPDATE SaviaSaludAuxiliar
       SET Estado =
            CASE
                WHEN ISNULL(NumeroTelefono,'') = ''
                 AND ISNULL(NumeroCelular,'') = ''
                 AND ISNULL(Email,'') = ''
                THEN 0
                ELSE 1
            END;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' REGISTROS ACTUALIZADOS';
    -------------------------------------------------
    -- 8. Insertar unicamente registros nuevos
    -------------------------------------------------
    INSERT INTO SaviaSalud
    (
        TitularID,
        FechaDeNacimiento,
        TipoAfiliado,
        EstadoDeAfiliacion,
        Regimen,
        NumeroTelefono,
        NumeroCelular,
        Estado,
        FechaConsulta
    )
    SELECT
        sa.TitularID,
        sa.FechaNacimiento,
        sa.TipoAfiliado,
        sa.EstadoAfiliacion,
        sa.Regimen,
        sa.NumeroTelefono,
        sa.NumeroCelular,
        sa.Estado,
        CAST(GETDATE() AS DATE)
    FROM SaviaSaludAuxiliar sa
    LEFT JOIN SaviaSalud ss
        ON ss.TitularID = sa.TitularID
       AND ISNULL(ss.NumeroTelefono,'') = ISNULL(sa.NumeroTelefono,'')
       AND ISNULL(ss.NumeroCelular,'') = ISNULL(sa.NumeroCelular,'')
       AND ISNULL(ss.TipoAfiliado,'') = ISNULL(sa.TipoAfiliado,'')
    WHERE ss.TitularID IS NULL;
    PRINT CAST(@@ROWCOUNT AS VARCHAR(20)) + ' PERSONAS INGRESADAS EN SAVIASALUD';
END;

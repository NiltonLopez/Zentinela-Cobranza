CREATE PROCEDURE [dbo].[sp_ProcesarNuevaEpsAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Filas INT;

    ------------------------------
    -- 1. Insertar en Titular si no existe
    ------------------------------
    INSERT INTO Titular (NumeroDocumento, Nombre)
    SELECT DISTINCT
        nea.TitularID,
        nea.Nombre
    FROM NuevaEpsAuxiliar nea
    LEFT JOIN Titular t
        ON nea.TitularID = t.NumeroDocumento
    WHERE t.NumeroDocumento IS NULL;

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' PERSONAS FUERON INGRESADAS A TITULAR';

    --------------------------------------------
    -- 2. Insertar en Titular_Cartera si no existe
    --------------------------------------------
    INSERT INTO Titular_Cartera
    (
        TitularID,
        CarteraID,
        LiderID
    )
    SELECT DISTINCT
        nea.TitularID,
        nea.CarteraID,
        nea.LiderID
    FROM NuevaEpsAuxiliar nea
    LEFT JOIN Titular_Cartera tc
        ON nea.TitularID = tc.TitularID
        AND nea.CarteraID = tc.CarteraID
        AND nea.LiderID = tc.LiderID
    WHERE tc.TitularID IS NULL;

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' PERSONAS FUERON RELACIONADAS EN TITULAR_CARTERA';

    ------------------------------
    -- 3. Eliminar duplicados en NuevaEpsAuxiliar
    ------------------------------
    ;WITH CTE_Duplicados AS
    (
        SELECT *,
               ROW_NUMBER() OVER
               (
                    PARTITION BY
                        TitularID,
                        NumeroTelefono,
                        EstadoAfiliacion,
                        TipoAfiliado
                    ORDER BY FechaConsulta DESC
               ) AS rn
        FROM NuevaEpsAuxiliar
    )
    DELETE FROM CTE_Duplicados
    WHERE rn > 1;

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' DUPLICADOS ELIMINADOS DE NUEVAEPSAUXILIAR';

    ---------------------------------------
    -- 4. Actualizar EstadoEnNuevaEps
    ---------------------------------------
    UPDATE NuevaEpsAuxiliar
    SET EstadoEnNuevaEps =
        CASE
            WHEN NumeroTelefono IS NULL
             AND EstadoAfiliacion IS NULL
             AND TipoAfiliado IS NULL
            THEN 0
            ELSE 1
        END;

    ---------------------------------------
    -- 5. Actualizar FechaConsulta
    ---------------------------------------
    UPDATE NuevaEpsAuxiliar
    SET FechaConsulta = GETDATE();

    ---------------------------------------
    -- 6. Insertar en NuevaEps
    ---------------------------------------
    INSERT INTO NuevaEps
    (
        TitularID,
        NumeroTelefono,
        EstadoAfiliacion,
        TipoAfiliado,
        FechaNacimiento,
        Departamento,
        Municipio,
        EstadoEnNuevaEps,
        FechaConsulta
    )
    SELECT
        TitularID,
        NumeroTelefono,
        EstadoAfiliacion,
        TipoAfiliado,
        FechaNacimiento,
        Departamento,
        Municipio,
        EstadoEnNuevaEps,
        FechaConsulta
    FROM NuevaEpsAuxiliar;

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' REGISTROS FUERON INGRESADOS A NUEVAEPS';

    -------------------------------------------
    -- 7. Eliminar duplicados en NuevaEps
    -------------------------------------------
    ;WITH CTE_Duplicados AS
    (
        SELECT *,
               ROW_NUMBER() OVER
               (
                    PARTITION BY
                        TitularID,
                        NumeroTelefono,
                        EstadoAfiliacion,
                        TipoAfiliado
                    ORDER BY FechaConsulta DESC
               ) AS rn
        FROM NuevaEps
    )
    DELETE FROM CTE_Duplicados
    WHERE rn > 1;

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' DUPLICADOS ELIMINADOS EN NUEVAEPS';

    -------------------------------------------
    -- 8. Eliminar registros vacíos si existe uno con información
    -------------------------------------------
    DELETE N
    FROM NuevaEps N
    WHERE EstadoEnNuevaEps = 0
      AND EXISTS
      (
            SELECT 1
            FROM NuevaEps X
            WHERE X.TitularID = N.TitularID
              AND (
                    X.NumeroTelefono IS NOT NULL
                    OR X.EstadoAfiliacion IS NOT NULL
                    OR X.TipoAfiliado IS NOT NULL
                  )
      );

    SET @Filas = @@ROWCOUNT;

    PRINT CAST(@Filas AS VARCHAR(20))
        + ' REGISTROS VACIOS ELIMINADOS';

END;

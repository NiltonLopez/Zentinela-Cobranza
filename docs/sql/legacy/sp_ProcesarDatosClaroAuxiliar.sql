CREATE PROCEDURE [dbo].[sp_ProcesarDatosClaroAuxiliar]
AS
BEGIN
    SET NOCOUNT ON;


	    ---------------------------------------
    -- 1. Actualizar fecha
    ---------------------------------------
    UPDATE DatosClaroAuxiliar
    SET FechaInsercion = GETDATE();


    ---------------------------------------
    -- 2. Limpiar telefonos con lista negra
    ---------------------------------------
    UPDATE D
    SET NumeroTelefono = NULL
    FROM DatosClaroAuxiliar D
    WHERE EXISTS (
        SELECT 1
        FROM ListaNegraTelefonosAuxiliar L
        WHERE D.NumeroTelefono = L.NumeroTelefono
    );

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' TELEFONOS EN LISTA NEGRA ELIMINADOS';


    ---------------------------------------
    -- 3. Limpiar correos con lista negra
    ---------------------------------------
    UPDATE D
    SET Email = NULL
    FROM DatosClaroAuxiliar D
    WHERE EXISTS (
        SELECT 1
        FROM ListaNegraCorreos L
        WHERE D.Email = L.CorreoErroneo
    );

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' CORREOS EN LISTA NEGRA ELIMINADOS';


    ---------------------------------------
    -- 4. Validar emails mal formados
    ---------------------------------------
    UPDATE DatosClaroAuxiliar
    SET Email = NULL
    WHERE Email NOT LIKE '%@%'
       OR Email NOT LIKE '%.%';

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' EMAILS INVALIDOS LIMPIADOS';


    ---------------------------------------
    -- 5. Eliminar registros vacios (en bloques)
    ---------------------------------------
    DECLARE @Filas INT = 1;

    WHILE @Filas > 0
    BEGIN
        DELETE TOP (5000)
        FROM DatosClaroAuxiliar
        WHERE NumeroTelefono IS NULL
          AND Email IS NULL;

        SET @Filas = @@ROWCOUNT;
    END

    PRINT 'REGISTROS VACIOS ELIMINADOS';


    ---------------------------------------
    -- 6. Insertar solo nuevos registros
    ---------------------------------------
    INSERT INTO DatosClaro (TitularID, NumeroTelefono, Email, FechaInsercion)
    SELECT D.TitularID, D.NumeroTelefono, D.Email, D.FechaInsercion
    FROM DatosClaroAuxiliar D
    WHERE NOT EXISTS (
        SELECT 1
        FROM DatosClaro C
        WHERE C.TitularID = D.TitularID
          AND ISNULL(C.NumeroTelefono,'') = ISNULL(D.NumeroTelefono,'')
          AND ISNULL(C.Email,'') = ISNULL(D.Email,'')
    );

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' REGISTROS INSERTADOS EN PRINCIPAL';


    ---------------------------------------
    -- 7. Eliminar duplicados (más eficiente)
    ---------------------------------------
    ;WITH CTE AS (
        SELECT *,
               ROW_NUMBER() OVER (
                   PARTITION BY TitularID, NumeroTelefono, Email
                   ORDER BY FechaInsercion DESC
               ) AS rn
        FROM DatosClaro
    )
    DELETE FROM CTE WHERE rn > 1;

    PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' DUPLICADOS ELIMINADOS';

END

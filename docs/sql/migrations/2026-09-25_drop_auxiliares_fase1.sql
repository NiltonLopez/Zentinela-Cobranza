-- ============================================================================
-- DROP de las tablas Auxiliar y sus SP de la Fase 1 — DBCallCenter (ADR-039)
--
--   ⚠ SE EJECUTA A MANO, Y SOLO DESPUÉS DE VALIDAR EL CARGUE EN PRODUCCIÓN.
--   No va con el despliegue del módulo. Primero se despliega Administración →
--   Cargue de Fuentes de Datos, se hace al menos un cargue real de cada una de
--   las seis fuentes, se revisa el resultado (conteos en CargueFuenteEjecucion,
--   dato visible en vw_BaseMadre / Calificador) y recién entonces se corre esto.
--   Mientras tanto el camino viejo (<Fuente>Auxiliar + sp_Procesar<Fuente>Auxiliar)
--   sigue disponible como plan B.
--
--   Qué borra (12 objetos, las 6 fuentes de la Fase 1):
--
--     Tabla staging          SP que la procesaba              Tabla final
--     SuraAuxiliar           sp_ProcesarSuraAuxiliar          Sura
--     CajacopiAuxiliar       sp_ProcesarCajacopiAuxiliar      Cajacopi
--     MutualSerAuxiliar      sp_ProcesarMutualSerAuxiliar     MutualSer
--     SIMITAuxiliar          sp_ProcesarSIMITAuxiliar         SIMIT
--     DatosClaroAuxiliar     sp_ProcesarDatosClaroAuxiliar    DatosClaro
--     NuevaEpsAuxiliar       sp_ProcesarNuevaEpsAuxiliar      NuevaEps
--
--   Las tablas finales NO se tocan: desde la Fase 1 las escribe el módulo.
--
--   Las definiciones están respaldadas en docs/sql/legacy/ (texto íntegro de
--   OBJECT_DEFINITION de los SP y CREATE de las tablas, tomados el 2026-09-23;
--   README con cómo restaurarlos). Este script lo COMPRUEBA: si el SP o la
--   estructura de la tabla en la BD ya no es la del respaldo (alguien los
--   modificó después), aborta. El rollback de esta migración es restaurar
--   desde docs/sql/legacy/ (primero las tablas, después los SP).
--
--   NO es parte de la Fase 1: ARLSuraAuxiliar / sp_ProcesarArlSuraAuxiliar.
--   Verificado el 2026-09-24 (solo lectura): los dos existen, la tabla tiene
--   0 filas y el SP sigue vivo alimentando dbo.ARLSura (1.565.455 filas,
--   leída por vw_BaseMadre, los tres fn_BaseMadre*, vw_DatosARLSura y otras).
--   ARL Sura es otra fuente (ADR-034) y se migra en la Fase 2 con las otras
--   15. Tampoco se tocan las demás *Auxiliar (Coosalud, Positiva, RUES,
--   Titular, ListaNegraTelefonos, etc.).
--
--   Guardas (THROW → no se borra nada, todo en una transacción):
--     1. Filas: si una tabla Auxiliar tiene aunque sea una fila, no se borra
--        (alguien la volvió a usar con el flujo viejo; averiguar antes).
--        Todas tenían 0 filas el 2026-09-23 y el 2026-09-24.
--     2. Respaldo: HASHBYTES de OBJECT_DEFINITION de cada SP y de la firma de
--        columnas de cada tabla contra los valores del respaldo en legacy/.
--     3. Dependencias: nada más que su propio SP puede referenciar una tabla,
--        y nada puede referenciar a los SP (sys.sql_expression_dependencies,
--        sys.dm_sql_referencing_entities, texto de sys.sql_modules para SQL
--        dinámico, sinónimos y pasos de SQL Agent en msdb). El 2026-09-24 el
--        único referenciador de cada tabla era su SP. No cubre referencias
--        desde OTRAS bases (se revisó a mano: src/ no usa ninguno de los 12).
--
--   Idempotente: un objeto que ya no existe se salta (DROP ... IF EXISTS y
--   guardas condicionadas a OBJECT_ID). Correrlo dos veces no falla.
--
--   Al terminar: actualizar docs/schema.md (las secciones de las 6 Auxiliar y
--   la tabla de SP pasan a "eliminadas el <fecha>").
--
--   Aplicar con: sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i <este archivo>
--   Fecha: 2026-09-25 (escrito el 2026-09-24; NO ejecutado).
-- ============================================================================

USE DBCallCenter;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Objetos TABLE (
    Tabla     sysname        NOT NULL PRIMARY KEY,
    Sp        sysname        NOT NULL,
    -- HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp)) = SHA-256 del texto de docs/sql/legacy/<sp>.sql
    -- (sin BOM, en UTF-16LE). Verificado igual en la BD y en el archivo el 2026-09-24.
    HashSp    varbinary(32)  NOT NULL,
    -- HASHBYTES('SHA2_256', STRING_AGG(nombre:tipo:max_length:is_nullable, '|') por column_id)
    HashTabla varbinary(32)  NOT NULL
);
INSERT INTO @Objetos (Tabla, Sp, HashSp, HashTabla) VALUES
    ('SuraAuxiliar',       'sp_ProcesarSuraAuxiliar',
     0x5165D12DB2F2EB6055B5386379250BE8D63ACE160CFB20D4E09C9F184978DD12,
     0x3581513329B049AB1F4F27E15A4A27E8FC9D57E7058827FF1378FA0AAEC5EC78),
    ('CajacopiAuxiliar',   'sp_ProcesarCajacopiAuxiliar',
     0xEF5DCCEEB38C813666027174EDADB76FC71C22BC25DF022E2F6618307057768E,
     0x51E28384EAFB8EC9CA15191570241D058CD2BF5E5251E15974C0448128ED8CB9),
    ('MutualSerAuxiliar',  'sp_ProcesarMutualSerAuxiliar',
     0x951708149D091166A91714735E70559BA4B91338221589C60D5BDAACF54234C0,
     0xE5BFA83555E84EBA6AE7C12DA8A18766A7575D428ECA6563DD6B6E087FD61ABB),
    ('SIMITAuxiliar',      'sp_ProcesarSIMITAuxiliar',
     0xAC819ACA21365288A67F45DE645B5558CD439917B7284E745D66CB83FCA4B67F,
     0x89ED600BBD929E9F2E8890DD4A1D5ECAE1DBEFEA4341ED209C6145CEC2C3D996),
    ('DatosClaroAuxiliar', 'sp_ProcesarDatosClaroAuxiliar',
     0xB8C376C6AB5F7A1485B57401A6F01560740CDDCC91CFBDC17653353AC92F0600,
     0xB58783190F9BF0B7D92C3E8DD9667218A7566ED39D69374B4F17FFD6B05FE2FF),
    ('NuevaEpsAuxiliar',   'sp_ProcesarNuevaEpsAuxiliar',
     0xF4319137013965F0BC675530B0F3BD300DE61E844AC9335E5C6CF14D9DC05148,
     0x93023A16D7EE706AB25F8E94F47DA601CD41A349F45F00C6BF7EA0E44D69B15C);

DECLARE @Msg nvarchar(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- ------------------------------------------------------------------------
    -- 1. Ninguna tabla tiene filas. TABLOCKX + HOLDLOCK: nadie puede insertar
    --    entre la comprobación y el DROP. IF anidado: si la tabla ya no existe,
    --    el SELECT no se compila (idempotencia).
    -- ------------------------------------------------------------------------
    IF OBJECT_ID(N'dbo.SuraAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SuraAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.SuraAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.CajacopiAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CajacopiAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.CajacopiAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.MutualSerAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.MutualSerAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.MutualSerAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.SIMITAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SIMITAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.SIMITAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.DatosClaroAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.DatosClaroAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.DatosClaroAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.NuevaEpsAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.NuevaEpsAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.NuevaEpsAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END

    -- ------------------------------------------------------------------------
    -- 2. Lo que se borra es exactamente lo respaldado en docs/sql/legacy/.
    -- ------------------------------------------------------------------------
    SET @Msg = (
        SELECT STRING_AGG(o.Sp, N', ')
          FROM @Objetos o
         WHERE OBJECT_ID(N'dbo.' + o.Sp, N'P') IS NOT NULL
           AND ISNULL(HASHBYTES('SHA2_256', OBJECT_DEFINITION(OBJECT_ID(N'dbo.' + o.Sp, N'P'))), 0x) <> o.HashSp);
    IF @Msg IS NOT NULL
    BEGIN
        SET @Msg = N'La definición de ' + @Msg + N' ya no es la del respaldo en docs/sql/legacy/: respaldar de nuevo antes de borrar.';
        THROW 51002, @Msg, 1;
    END

    SET @Msg = (
        SELECT STRING_AGG(o.Tabla, N', ')
          FROM @Objetos o
         CROSS APPLY (SELECT HASHBYTES('SHA2_256',
                         (SELECT STRING_AGG(CONCAT(c.name, ':', ty.name, ':', c.max_length, ':', c.is_nullable), N'|')
                                     WITHIN GROUP (ORDER BY c.column_id)
                            FROM sys.columns c
                            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                           WHERE c.object_id = OBJECT_ID(N'dbo.' + o.Tabla, N'U'))) AS Hash) h
         WHERE OBJECT_ID(N'dbo.' + o.Tabla, N'U') IS NOT NULL
           AND ISNULL(h.Hash, 0x) <> o.HashTabla);
    IF @Msg IS NOT NULL
    BEGIN
        SET @Msg = N'La estructura de ' + @Msg + N' ya no es la del respaldo en docs/sql/legacy/: respaldar de nuevo antes de borrar.';
        THROW 51003, @Msg, 1;
    END

    -- ------------------------------------------------------------------------
    -- 3. Dependencias: nadie más que su propio SP usa cada tabla; nadie usa los SP.
    -- ------------------------------------------------------------------------
    DECLARE @Deps TABLE (Quien nvarchar(400) NOT NULL, Que sysname NOT NULL);

    -- 3a. Dependencias registradas por nombre (incluye referencias no resueltas).
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(OBJECT_SCHEMA_NAME(d.referencing_id), N'.', OBJECT_NAME(d.referencing_id)), d.referenced_entity_name
      FROM sys.sql_expression_dependencies d
      JOIN @Objetos o ON d.referenced_entity_name IN (o.Tabla, o.Sp)
     WHERE ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
       AND d.referenced_database_name IS NULL
       AND NOT (d.referenced_entity_name = o.Tabla AND d.referencing_id = OBJECT_ID(N'dbo.' + o.Sp));

    -- 3b. Mismo criterio desde el lado del objeto referenciado.
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(r.referencing_schema_name, N'.', r.referencing_entity_name), o.Tabla
      FROM @Objetos o
     CROSS APPLY sys.dm_sql_referencing_entities(N'dbo.' + o.Tabla, N'OBJECT') r
     WHERE r.referencing_id <> ISNULL(OBJECT_ID(N'dbo.' + o.Sp), 0);
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(r.referencing_schema_name, N'.', r.referencing_entity_name), o.Sp
      FROM @Objetos o
     CROSS APPLY sys.dm_sql_referencing_entities(N'dbo.' + o.Sp, N'OBJECT') r;

    -- 3c. SQL dinámico: el nombre como palabra completa en el texto de otro módulo.
    --     (Con bordes de palabra: 'ARLSuraAuxiliar' contiene 'SuraAuxiliar' y no debe contar.)
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(OBJECT_SCHEMA_NAME(m.object_id), N'.', OBJECT_NAME(m.object_id)), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN sys.sql_modules m
        ON N' ' + m.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%'
     WHERE m.object_id <> ISNULL(OBJECT_ID(N'dbo.' + o.Sp), 0);

    -- 3d. Sinónimos y pasos del Agente.
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(N'sinónimo ', SCHEMA_NAME(s.schema_id), N'.', s.name), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN sys.synonyms s
        ON N' ' + s.base_object_name + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%';
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(N'job ', j.name, N' / paso ', js.step_name), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN msdb.dbo.sysjobsteps js
        ON N' ' + js.command + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%'
      JOIN msdb.dbo.sysjobs j ON j.job_id = js.job_id;

    SET @Msg = (SELECT STRING_AGG(CONCAT(Quien, N' → ', Que), N'; ')
                  FROM (SELECT DISTINCT Quien, Que FROM @Deps) d);
    IF @Msg IS NOT NULL
    BEGIN
        SET @Msg = LEFT(N'Hay objetos que todavía usan lo que se va a borrar: ' + @Msg, 2048);
        THROW 51004, @Msg, 1;
    END

    -- ------------------------------------------------------------------------
    -- 4. DROP: primero los SP (dependen de las tablas), después las tablas.
    -- ------------------------------------------------------------------------
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarSuraAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarCajacopiAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarMutualSerAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarSIMITAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarDatosClaroAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarNuevaEpsAuxiliar;

    DROP TABLE IF EXISTS dbo.SuraAuxiliar;
    DROP TABLE IF EXISTS dbo.CajacopiAuxiliar;
    DROP TABLE IF EXISTS dbo.MutualSerAuxiliar;
    DROP TABLE IF EXISTS dbo.SIMITAuxiliar;
    DROP TABLE IF EXISTS dbo.DatosClaroAuxiliar;
    DROP TABLE IF EXISTS dbo.NuevaEpsAuxiliar;

    -- ------------------------------------------------------------------------
    -- 5. Verificación: no queda ninguno de los 12. ARLSura queda intacta.
    -- ------------------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM @Objetos o
                WHERE OBJECT_ID(N'dbo.' + o.Tabla, N'U') IS NOT NULL
                   OR OBJECT_ID(N'dbo.' + o.Sp, N'P') IS NOT NULL)
        THROW 51005, N'Quedó algún objeto sin borrar: se revierte todo.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Verificación visible: las 6 tablas y los 6 SP ya no existen; ARLSuraAuxiliar y su SP siguen.
SELECT name, type_desc
  FROM sys.objects
 WHERE name IN ('SuraAuxiliar', 'CajacopiAuxiliar', 'MutualSerAuxiliar', 'SIMITAuxiliar',
                'DatosClaroAuxiliar', 'NuevaEpsAuxiliar', 'ARLSuraAuxiliar',
                'sp_ProcesarSuraAuxiliar', 'sp_ProcesarCajacopiAuxiliar', 'sp_ProcesarMutualSerAuxiliar',
                'sp_ProcesarSIMITAuxiliar', 'sp_ProcesarDatosClaroAuxiliar', 'sp_ProcesarNuevaEpsAuxiliar',
                'sp_ProcesarArlSuraAuxiliar')
 ORDER BY name;
GO

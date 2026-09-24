-- ============================================================================
-- DROP de las tablas Auxiliar y sus SP de la Fase 2 — DBCallCenter (ADR-039)
--
--   ⚠ NO EJECUTAR HASTA VALIDAR EL CARGUE EN PRODUCCIÓN. SE EJECUTA A MANO.
--   No va con el despliegue del módulo. Primero se despliega Administración →
--   Cargue de Fuentes de Datos con las 22 fuentes, se hace al menos un cargue
--   real de cada fuente de la Fase 2, se revisa el resultado (conteos en
--   CargueFuenteEjecucion, dato visible en vw_BaseMadre / Calificador) y recién
--   entonces se corre esto. Mientras tanto el camino viejo
--   (<Fuente>Auxiliar + sp_Procesar<Fuente>Auxiliar) sigue disponible como
--   plan B. Es independiente del DROP de la Fase 1
--   (2026-09-25_drop_auxiliares_fase1.sql): se pueden correr en cualquier orden.
--
--   Qué borra (31 objetos: 16 tablas y 15 SP):
--
--     Tabla staging              SP que la procesaba                   Tabla final
--     ARLSuraAuxiliar            sp_ProcesarArlSuraAuxiliar            ARLSura
--     CamaraAuxiliar             sp_ProcesarCamaraAuxiliar             Camara
--     CamaraEmpresaAuxiliar      sp_ProcesarCamaraEmpresaAuxiliar      CamaraEmpresa
--     CompensadosAuxiliar        sp_ProcesarCompensadosAuxiliar        Compensados
--     CoosaludAuxiliar           sp_ProcesarCoosaludAuxiliar           Coosalud
--     FosygaAuxiliar             sp_ProcesarFosygaAuxiliar             Fosyga
--     MiVacunaAuxiliar           sp_ProcesarMiVacunaAuxiliar           MiVacuna
--     PositivaAuxiliar           sp_ProcesarPositivaAuxiliar           Positiva
--     PositivaEmpresasAuxiliar   sp_ProcesarPositivaEmpresasAuxiliar   PositivaEmpresas
--     RGMAuxiliar                sp_ProcesarRGMAuxiliar                RGM
--     RUESAuxiliar               sp_ProcesarRUESAuxiliar               RUES
--     SaludTotalAuxiliar         sp_ProcesarSaludTotalAuxiliar         SaludTotal
--     SaviaSaludAuxiliar         sp_ProcesarSaviaSaludAuxiliar         SaviaSalud
--     SOSAuxiliar                sp_ProcesarSOSAuxiliar                SOS
--     TigoUneAuxiliar            sp_ProcesarTigoUneAuxiliar            TigoUne
--     TigoAuxiliar               (no tiene SP: Tigo se llenó a mano)   Tigo
--
--   Las tablas finales NO se tocan: desde la Fase 2 las escribe el módulo.
--
--   Las definiciones están respaldadas en docs/sql/legacy/ (texto íntegro de
--   OBJECT_DEFINITION de los 15 SP y CREATE de las 16 tablas, tomados el
--   2026-09-24; README con cómo restaurarlos). Este script lo COMPRUEBA: si un
--   SP o la estructura de una tabla en la BD ya no es la del respaldo (alguien
--   los modificó después), aborta. El rollback de esta migración es restaurar
--   desde docs/sql/legacy/ (primero las tablas, después los SP). Los respaldos
--   son de ESTRUCTURA: las filas que haya en una Auxiliar no se respaldan.
--
--   ─── HOY TRES AUXILIAR TIENEN FILAS: LA GUARDA 1 VA A BLOQUEAR EL DROP ───
--   Conteo del 2026-09-24 (solo lectura; detalle en
--   docs/sql/legacy/PARIDAD-otras.md, sección "Auxiliar con datos"):
--
--     FosygaAuxiliar   18.598 filas   Carga FALLIDA del 2026-09-23 11:33: el SP
--                                     corrió pero ni Titular ni Fosyga recibieron
--                                     las filas (Nombre, CarteraID y LiderID en
--                                     NULL; 4.124 titulares inexistentes → FK).
--                                     ~18.053 filas de afiliación NO están en
--                                     dbo.Fosyga. NO es residuo: antes de vaciarla
--                                     hay que hablar con quien carga Fosyga (lo
--                                     está haciendo a mano esta semana) y decidir
--                                     si se recargan por el módulo con un archivo
--                                     que traiga el nombre.
--     RGMAuxiliar      30.144 filas   Residuo de la carga del 2026-08-31: el
--                                     100 % de su contenido (Marca, Modelo, Placa)
--                                     ya está en dbo.RGM con esa fecha y las
--                                     30.144 relaciones (14, 28) ya existen.
--     TigoAuxiliar    661.072 filas   Residuo de feb-mar de 2025: los 661.072
--                                     titulares están en dbo.Tigo, 661.005 con el
--                                     mismo contenido.
--
--   Vaciarlas es una decisión del USUARIO, con confirmación explícita, y NO la
--   toma este script (no tiene ningún DELETE/TRUNCATE sobre ellas). Antes de
--   pedir la confirmación, repetir la comprobación de que el contenido está en
--   el destino (las cifras de arriba son del 2026-09-24), p. ej. para RGM:
--       SELECT COUNT(*) FROM dbo.RGMAuxiliar a
--        WHERE NOT EXISTS (SELECT 1 FROM dbo.RGM r WHERE r.TitularID = a.TitularID
--                            AND EXISTS (SELECT r.Marca, r.Modelo, r.Placa
--                                        INTERSECT SELECT a.Marca, TRY_CONVERT(int, a.Modelo), a.Placa));
--       -- el 2026-09-24 daba 0
--   y lo mismo para Tigo con (NumeroTelefono, Email): el 2026-09-24 daba 67
--   (titulares que están en Tigo pero con OTRO teléfono/correo; ningún titular
--   falta en Tigo). Esas 67 variantes se pierden al vaciar: decírselo al usuario
--   al pedir la confirmación. Con el sí del usuario, se
--   vacían a mano (TRUNCATE TABLE dbo.RGMAuxiliar / dbo.TigoAuxiliar; y
--   dbo.FosygaAuxiliar solo cuando se haya resuelto su carga) y después se
--   corre este script. Otra fila que aparezca en cualquiera de las 16 significa
--   que alguien volvió a usar el flujo viejo: averiguar antes.
--
--   ─── QUÉ NO SE BORRA, Y POR QUÉ ───
--     TitularAuxiliar (47.531 filas) + sp_ProcesarTitularAuxiliar: carga de
--         titulares y asignaciones (Titular + Titular_Cartera), no una fuente
--         de localización. Sigue en uso (modificada 2026-04-18) y la lee
--         vw_BaseMadreTitularAuxiliar. Fuera del alcance de ADR-039.
--     Titular_CarteraAuxiliar (2 filas): staging de asignaciones de cartera,
--         mismo dominio que TitularAuxiliar; ningún cargue de fuentes la usa.
--     ListaNegraTelefonosAuxiliar (1.204.923 filas): staging de la lista
--         negra de TELÉFONOS. Su único lector en la BD era
--         sp_ProcesarDatosClaroAuxiliar (Fase 1; el motor cruza contra
--         ListaNegraTelefonos real). Qué hacer con ella lo decide el proceso de
--         lista negra (Staging_ListaNegraTelefonos + sp_InsertarListaNegraTelefonos),
--         no este módulo.
--     DatosExternosAuxiliar (1.046.532 filas): staging de DatosExternos, fuente
--         que Zentinela no consume (fuera de la BaseMadre desde 2026-05-07) y
--         que no está en el catálogo del cargue. Borrarla es otra decisión.
--     RuntAuxiliar (0 filas): Runt NO se agregó al cargue (dbo.Runt y
--         RuntAuxiliar vacías, sin SP ni consumidores). Si Runt se activa, su
--         staging se reemplaza por el módulo; si se abandona, se borran Runt y
--         RuntAuxiliar juntas. En los dos casos es una decisión aparte.
--     Staging_* (Staging_UltimoEstadoTitular, Staging_ListaNegraTelefonos,
--         Staging_UltimoEstadoTitular_Respaldo_20260923_1444): no son del flujo
--         <Fuente>Auxiliar. Las dos primeras son la zona de aterrizaje de los
--         ETL vivos (Estado Titular, ADR-038; lista negra) y la tercera es el
--         respaldo del incidente del 2026-09-23 (ADR-038).
--
--   Guardas (THROW → no se borra nada, todo en una transacción):
--     1. Filas: si una tabla Auxiliar tiene aunque sea una fila, no se borra
--        (ver arriba: hoy Fosyga, RGM y Tigo la tienen).
--     2. Respaldo: HASHBYTES de OBJECT_DEFINITION de cada SP y de la firma de
--        columnas de cada tabla contra los valores del respaldo en legacy/
--        (verificados iguales en la BD y en los archivos el 2026-09-24).
--     3. Dependencias: nada más que los SP de ESTA lista puede referenciar una
--        tabla, y nada fuera de esta lista puede referenciar a los SP
--        (sys.sql_expression_dependencies, sys.dm_sql_referencing_entities,
--        texto de sys.sql_modules para SQL dinámico, sinónimos y pasos de SQL
--        Agent en msdb). A diferencia de la Fase 1, se excluyen TODOS los SP
--        que se borran aquí, no solo el propio: sp_ProcesarSaludTotalAuxiliar
--        nombra RUESAuxiliar en un comentario ("-- 6. Update el campo
--        FechaConsulta en RUESAuxiliar", copia y pega) y la búsqueda de texto
--        lo contaría. El 2026-09-24 esa era la única coincidencia. No cubre
--        referencias desde OTRAS bases (se revisó a mano: src/ no usa ninguno
--        de los 31; solo los nombra en comentarios de las reglas).
--
--   Idempotente: un objeto que ya no existe se salta (DROP ... IF EXISTS y
--   guardas condicionadas a OBJECT_ID). Correrlo dos veces no falla.
--
--   Al terminar: actualizar docs/schema.md (las secciones de las 16 Auxiliar y
--   la tabla de SP pasan a "eliminadas el <fecha>").
--
--   Aplicar con: sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i <este archivo>
--   Fecha: 2026-09-26 (escrito el 2026-09-24; NO ejecutado).
-- ============================================================================

USE DBCallCenter;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Objetos TABLE (
    Tabla     sysname        NOT NULL PRIMARY KEY,
    Sp        sysname        NULL,       -- NULL = la tabla no tiene SP (TigoAuxiliar)
    -- HASHBYTES('SHA2_256', OBJECT_DEFINITION(sp)) = SHA-256 del texto de docs/sql/legacy/<sp>.sql
    -- (sin BOM, en UTF-16LE). Verificado igual en la BD y en el archivo el 2026-09-24.
    HashSp    varbinary(32)  NULL,
    -- HASHBYTES('SHA2_256', STRING_AGG(nombre:tipo:max_length:is_nullable, '|') por column_id),
    -- tomada de la BD el 2026-09-24; coincide con docs/sql/legacy/<Tabla>_CREATE.sql.
    HashTabla varbinary(32)  NOT NULL
);
INSERT INTO @Objetos (Tabla, Sp, HashSp, HashTabla) VALUES
    ('ARLSuraAuxiliar',          'sp_ProcesarArlSuraAuxiliar',
     0x2C5988BEEFFB19478C4BD9E9F74F97AE7E11931C619369D59409E980B6B67800,
     0xAAF3CF9BD0A7BA18A99C2D86E96640C3CC9D6D87C38A5B3B7736C9AD32E8D0EC),
    ('CamaraAuxiliar',           'sp_ProcesarCamaraAuxiliar',
     0x18080BABE31FB517C6D0592BCCA920059A965DD857757C0751A9E6E4903A579D,
     0x60C681A2121A9444CC6E3F9CA8621FB5F27B6E3690B6EED57D8F16B6D9A2E4B4),
    ('CamaraEmpresaAuxiliar',    'sp_ProcesarCamaraEmpresaAuxiliar',
     0xFB3D13D7F12E41CAD1C99B712B3FAA9524E28815406597D2BCF7167B575318FB,
     0xFBC6775683D244AFC3D6D1F4BD16E96A4C53414B499969ADC584D61D740D32B9),
    ('CompensadosAuxiliar',      'sp_ProcesarCompensadosAuxiliar',
     0x72FC5F9CBD1AAC6079447D4537FBA1D78792A672A685C8FA5C5C4C8D80D5A6DC,
     0x026DABEF84881C57730408CC4DE557CBF6FD01980ADD669789CABE8FA0E2CE89),
    ('CoosaludAuxiliar',         'sp_ProcesarCoosaludAuxiliar',
     0x1DD29FB7200D8CA53683D835AF76081D8A25CAC78A9AC0C47239F4287097294F,
     0xDAD7BBADB872C945FA14627FA0AAEED0DEE0DCCE6F17CCE8C8EE60DB8A3A06D6),
    ('FosygaAuxiliar',           'sp_ProcesarFosygaAuxiliar',
     0x7AA668181D8C451236BA84CDA1044294BC2A051B9D46D1C365D011D73129BB68,
     0xBB9E1A5C4C09CAB1E44497F65CC5DCE63CBD3940C8C1BC2ADFDC5566408CD4D0),
    ('MiVacunaAuxiliar',         'sp_ProcesarMiVacunaAuxiliar',
     0x0FE969D6439F7CE0441EC53D4CD72CD95E2784C0C7A67021310A7662D73A7A87,
     0xA296DD1D7DB0D83A092845951416931ADEB685BDFCEE75DA0FDBB2A5BAAC7623),
    ('PositivaAuxiliar',         'sp_ProcesarPositivaAuxiliar',
     0xC68AA56ED263BEC20D9186EE38EB15D6CE1EED6AB41230BF7014FF65B89C74CD,
     0x613713D7CEB85D32BE809E161A16E41329CFEF76248E1B95272A51E001D1827E),
    ('PositivaEmpresasAuxiliar', 'sp_ProcesarPositivaEmpresasAuxiliar',
     0xA3D99FB9FEF3254C183918260AFBA5C28AD792094F685280FE34B6E6A6A595A6,
     0xD253D625B966900427B4F8814204BE0738273239622A62A748338D6667C4A0EA),
    ('RGMAuxiliar',              'sp_ProcesarRGMAuxiliar',
     0x8353CDACEBB995FD2327D91E1E51DB738AEFCF0EC769D1B242CB226F33954C22,
     0xC10DC080FE23B984B424806624D65B4998364700EC8C683C344FEBC72D947167),
    ('RUESAuxiliar',             'sp_ProcesarRUESAuxiliar',
     0x09B1AE78919AF507F54F6B4107CA7C49F2D97F50A16D62B1FFBED9B9EED02016,
     0xC423EDA81B3BFF5082F7AA907B161C2D86E6E2C45088189229F71014797D3A8A),
    ('SaludTotalAuxiliar',       'sp_ProcesarSaludTotalAuxiliar',
     0x0FBDC0FA1E08D5793806E61590E9C23CDD57476F9FE9C1CE015A24CF702704EB,
     0x021C331EA8C40E62D81F6D18BA867A4D5FEE0C93FFEB4028E66AB6C94BC26A0E),
    ('SaviaSaludAuxiliar',       'sp_ProcesarSaviaSaludAuxiliar',
     0xEF03241D60D8B89A4E8EFFB3C52397414FD4C5B0748A22864039E172C801A0BF,
     0x85DA2F5134181E54369A01ED4872B2D20F02E400D5D4E7A66027ADD078E6222C),
    ('SOSAuxiliar',              'sp_ProcesarSOSAuxiliar',
     0x28564ED369BAA616EB5DA9FD1D99C19B6A1D6FF0F5167CDDA554DC83800422D1,
     0xEDFE4BDBB21AA537EB1F3825EA98BDA79D1A6E40E5E7150C5EDEBF9F75DB6F61),
    ('TigoUneAuxiliar',          'sp_ProcesarTigoUneAuxiliar',
     0x8994851AB6DBEDA5AB2E41D3F4C4E5EAC156E3A3AB7A28D385324ADC848EA42B,
     0xC0590B5DDB6464139A1C4E6A03ECC99E00B06981556120928C35BF83C1A8ADA0),
    ('TigoAuxiliar',             NULL,
     NULL,
     0xF62295507F5CC93BA68553128E7FE62156B7AEFAC1C98EA0D4AD38FADA182459);

DECLARE @Msg nvarchar(2048);

BEGIN TRY
    BEGIN TRANSACTION;

    -- ------------------------------------------------------------------------
    -- 1. Ninguna tabla tiene filas. TABLOCKX + HOLDLOCK: nadie puede insertar
    --    entre la comprobación y el DROP. IF anidado: si la tabla ya no existe,
    --    el SELECT no se compila (idempotencia).
    -- ------------------------------------------------------------------------
    IF OBJECT_ID(N'dbo.ARLSuraAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.ARLSuraAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.ARLSuraAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.CamaraAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CamaraAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.CamaraAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.CamaraEmpresaAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CamaraEmpresaAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.CamaraEmpresaAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.CompensadosAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CompensadosAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.CompensadosAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.CoosaludAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.CoosaludAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.CoosaludAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.FosygaAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.FosygaAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.FosygaAuxiliar tiene filas: no se borra. El 2026-09-24 guardaba una carga FALLIDA del 2026-09-23 que no está en Fosyga: hablar con quien carga Fosyga antes de vaciarla (ver cabecera).', 1;
    END
    IF OBJECT_ID(N'dbo.MiVacunaAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.MiVacunaAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.MiVacunaAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.PositivaAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.PositivaAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.PositivaAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.PositivaEmpresasAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.PositivaEmpresasAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.PositivaEmpresasAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.RGMAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.RGMAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.RGMAuxiliar tiene filas: no se borra. El 2026-09-24 era residuo ya contenido en RGM: comprobarlo de nuevo y vaciarla con confirmación del usuario (ver cabecera).', 1;
    END
    IF OBJECT_ID(N'dbo.RUESAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.RUESAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.RUESAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.SaludTotalAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SaludTotalAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.SaludTotalAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.SaviaSaludAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SaviaSaludAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.SaviaSaludAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.SOSAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SOSAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.SOSAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.TigoUneAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.TigoUneAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.TigoUneAuxiliar tiene filas: no se borra. Alguien usó el flujo viejo; averiguar antes.', 1;
    END
    IF OBJECT_ID(N'dbo.TigoAuxiliar', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.TigoAuxiliar WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, N'dbo.TigoAuxiliar tiene filas: no se borra. El 2026-09-24 era residuo de 2025 ya contenido en Tigo: comprobarlo de nuevo y vaciarla con confirmación del usuario (ver cabecera).', 1;
    END

    -- ------------------------------------------------------------------------
    -- 2. Lo que se borra es exactamente lo respaldado en docs/sql/legacy/.
    -- ------------------------------------------------------------------------
    SET @Msg = (
        SELECT STRING_AGG(o.Sp, N', ')
          FROM @Objetos o
         WHERE o.Sp IS NOT NULL
           AND OBJECT_ID(N'dbo.' + o.Sp, N'P') IS NOT NULL
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
    -- 3. Dependencias: solo los SP que se borran aquí usan las tablas; nadie
    --    de afuera usa los SP. @SpsQueSeBorran: los object_id de los 15 SP.
    -- ------------------------------------------------------------------------
    DECLARE @SpsQueSeBorran TABLE (Id int NOT NULL PRIMARY KEY);
    INSERT INTO @SpsQueSeBorran (Id)
    SELECT OBJECT_ID(N'dbo.' + o.Sp, N'P') FROM @Objetos o
     WHERE o.Sp IS NOT NULL AND OBJECT_ID(N'dbo.' + o.Sp, N'P') IS NOT NULL;

    DECLARE @Deps TABLE (Quien nvarchar(400) NOT NULL, Que sysname NOT NULL);

    -- 3a. Dependencias registradas por nombre (incluye referencias no resueltas).
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(OBJECT_SCHEMA_NAME(d.referencing_id), N'.', OBJECT_NAME(d.referencing_id)), d.referenced_entity_name
      FROM sys.sql_expression_dependencies d
      JOIN @Objetos o ON d.referenced_entity_name IN (o.Tabla, o.Sp)
     WHERE ISNULL(d.referenced_schema_name, N'dbo') = N'dbo'
       AND d.referenced_database_name IS NULL
       AND d.referencing_id NOT IN (SELECT Id FROM @SpsQueSeBorran);

    -- 3b. Mismo criterio desde el lado del objeto referenciado.
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(r.referencing_schema_name, N'.', r.referencing_entity_name), o.Tabla
      FROM @Objetos o
     CROSS APPLY sys.dm_sql_referencing_entities(N'dbo.' + o.Tabla, N'OBJECT') r
     WHERE r.referencing_id NOT IN (SELECT Id FROM @SpsQueSeBorran);
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(r.referencing_schema_name, N'.', r.referencing_entity_name), o.Sp
      FROM @Objetos o
     CROSS APPLY sys.dm_sql_referencing_entities(N'dbo.' + o.Sp, N'OBJECT') r
     WHERE o.Sp IS NOT NULL
       AND r.referencing_id NOT IN (SELECT Id FROM @SpsQueSeBorran);

    -- 3c. SQL dinámico: el nombre como palabra completa en el texto de otro módulo.
    --     (Con bordes de palabra: 'TigoUneAuxiliar' no contiene 'TigoAuxiliar', pero así no
    --     depende de eso.)
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(OBJECT_SCHEMA_NAME(m.object_id), N'.', OBJECT_NAME(m.object_id)), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN sys.sql_modules m
        ON N' ' + m.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%'
     WHERE x.Nombre IS NOT NULL
       AND m.object_id NOT IN (SELECT Id FROM @SpsQueSeBorran);

    -- 3d. Sinónimos y pasos del Agente.
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(N'sinónimo ', SCHEMA_NAME(s.schema_id), N'.', s.name), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN sys.synonyms s
        ON N' ' + s.base_object_name + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%'
     WHERE x.Nombre IS NOT NULL;
    INSERT INTO @Deps (Quien, Que)
    SELECT CONCAT(N'job ', j.name, N' / paso ', js.step_name), x.Nombre
      FROM @Objetos o
     CROSS APPLY (VALUES (o.Tabla), (o.Sp)) x(Nombre)
      JOIN msdb.dbo.sysjobsteps js
        ON N' ' + js.command + N' ' LIKE N'%[^A-Za-z0-9_]' + x.Nombre + N'[^A-Za-z0-9_]%'
      JOIN msdb.dbo.sysjobs j ON j.job_id = js.job_id
     WHERE x.Nombre IS NOT NULL;

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
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarArlSuraAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarCamaraAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarCamaraEmpresaAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarCompensadosAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarCoosaludAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarFosygaAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarMiVacunaAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarPositivaAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarPositivaEmpresasAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarRGMAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarRUESAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarSaludTotalAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarSaviaSaludAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarSOSAuxiliar;
    DROP PROCEDURE IF EXISTS dbo.sp_ProcesarTigoUneAuxiliar;

    DROP TABLE IF EXISTS dbo.ARLSuraAuxiliar;
    DROP TABLE IF EXISTS dbo.CamaraAuxiliar;
    DROP TABLE IF EXISTS dbo.CamaraEmpresaAuxiliar;
    DROP TABLE IF EXISTS dbo.CompensadosAuxiliar;
    DROP TABLE IF EXISTS dbo.CoosaludAuxiliar;
    DROP TABLE IF EXISTS dbo.FosygaAuxiliar;
    DROP TABLE IF EXISTS dbo.MiVacunaAuxiliar;
    DROP TABLE IF EXISTS dbo.PositivaAuxiliar;
    DROP TABLE IF EXISTS dbo.PositivaEmpresasAuxiliar;
    DROP TABLE IF EXISTS dbo.RGMAuxiliar;
    DROP TABLE IF EXISTS dbo.RUESAuxiliar;
    DROP TABLE IF EXISTS dbo.SaludTotalAuxiliar;
    DROP TABLE IF EXISTS dbo.SaviaSaludAuxiliar;
    DROP TABLE IF EXISTS dbo.SOSAuxiliar;
    DROP TABLE IF EXISTS dbo.TigoUneAuxiliar;
    DROP TABLE IF EXISTS dbo.TigoAuxiliar;

    -- ------------------------------------------------------------------------
    -- 5. Verificación: no queda ninguno de los 31. Las tablas finales quedan intactas.
    -- ------------------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM @Objetos o
                WHERE OBJECT_ID(N'dbo.' + o.Tabla, N'U') IS NOT NULL
                   OR (o.Sp IS NOT NULL AND OBJECT_ID(N'dbo.' + o.Sp, N'P') IS NOT NULL))
        THROW 51005, N'Quedó algún objeto sin borrar: se revierte todo.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Verificación visible: las 16 Auxiliar y los 15 SP ya no existen; las que quedan fuera siguen.
SELECT name, type_desc
  FROM sys.objects
 WHERE name IN ('ARLSuraAuxiliar', 'CamaraAuxiliar', 'CamaraEmpresaAuxiliar', 'CompensadosAuxiliar',
                'CoosaludAuxiliar', 'FosygaAuxiliar', 'MiVacunaAuxiliar', 'PositivaAuxiliar',
                'PositivaEmpresasAuxiliar', 'RGMAuxiliar', 'RUESAuxiliar', 'SaludTotalAuxiliar',
                'SaviaSaludAuxiliar', 'SOSAuxiliar', 'TigoUneAuxiliar', 'TigoAuxiliar',
                'sp_ProcesarArlSuraAuxiliar', 'sp_ProcesarCamaraAuxiliar', 'sp_ProcesarCamaraEmpresaAuxiliar',
                'sp_ProcesarCompensadosAuxiliar', 'sp_ProcesarCoosaludAuxiliar', 'sp_ProcesarFosygaAuxiliar',
                'sp_ProcesarMiVacunaAuxiliar', 'sp_ProcesarPositivaAuxiliar', 'sp_ProcesarPositivaEmpresasAuxiliar',
                'sp_ProcesarRGMAuxiliar', 'sp_ProcesarRUESAuxiliar', 'sp_ProcesarSaludTotalAuxiliar',
                'sp_ProcesarSaviaSaludAuxiliar', 'sp_ProcesarSOSAuxiliar', 'sp_ProcesarTigoUneAuxiliar',
                -- fuera de esta migración (deben seguir):
                'TitularAuxiliar', 'Titular_CarteraAuxiliar', 'ListaNegraTelefonosAuxiliar',
                'DatosExternosAuxiliar', 'RuntAuxiliar', 'sp_ProcesarTitularAuxiliar')
 ORDER BY name;
GO

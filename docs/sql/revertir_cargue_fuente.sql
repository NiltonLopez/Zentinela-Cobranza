-- ============================================================================
-- Revertir UN cargue del módulo Cargue de Fuentes de Datos — DBCallCenter
-- (ADR-039)
--
--   Script MANUAL. No hay botón en la UI: revertir es excepcional y lo decide
--   una persona mirando los conteos. Se parametriza con @EjecucionID
--   (dbo.CargueFuenteEjecucion) y usa lo que el motor dejó en
--   dbo.CargueFuenteReversion dentro de la misma transacción del cargue.
--
--   Por defecto es un ENSAYO: hace todo, muestra los conteos y hace ROLLBACK.
--   Solo con @Confirmar = 1 hace COMMIT.
--
--   Qué deshace, por acción registrada:
--
--     'I' insertada    Se BORRA la fila del destino. Se ubica por la clave de
--                      la fila (TitularID; NitEmpresa en CamaraEmpresa) + hash
--                      del contenido (recalculado con la MISMA expresión que
--                      GeneradorSqlCargue.HashContenido) + fecha de sello
--                      = SelloFechaConsulta de la ejecución.
--     'U' refrescada   Se le DEVUELVE la fecha que tenía (FechaAnterior). Su
--                      contenido no cambió: el cargue solo le movió la fecha.
--     'D' vacía        Se REINSERTA como fila vacía: clave + flag 0 + fecha
--         purgada      original. Limitación honesta: de la fila purgada solo
--                      se guardó la fecha (el hash de 'D' es 0x00), así que
--                      las columnas que NO entran al flag y que pudo tener
--                      (Departamento, Municipio, Regimen, TipoAfiliado,
--                      FechaNacimiento, EstadoAfiliacion, NombreEmpresa,
--                      Cargo, IngresoBase… según la fuente) vuelven en NULL.
--                      Para SIMIT la reconstrucción es exacta (su única
--                      columna de dato es Placa, NULL en una vacía).
--                      Con @ReponerVacias = 0 no se reinsertan.
--
--   Qué NO deshace, y por qué:
--     - Titulares creados en dbo.Titular, nombres reparados en Titular.Nombre y
--       relaciones nuevas en dbo.Titular_Cartera. Son reparaciones ADITIVAS que
--       otras fuentes, cargues y gestiones pueden estar usando desde entonces;
--       borrarlas rompería FKs o devolvería nombres con mojibake. Además la
--       tabla de reversión no las registra: el motor solo guarda lo que tocó
--       en la tabla de la fuente.
--     - La caché del Calificador (en memoria de la API). Tras un COMMIT
--       reciclar el AppPool o esperar el corte diario de la 1 p. m.; si no, el
--       Calificador puede mostrar hasta 24 h las cifras con el cargue.
--
--   Guardas (THROW, no se toca nada):
--     - la ejecución no existe, no está en 'OK', ya fue revertida, o su fuente
--       no está en la lista blanca de abajo;
--     - hay un cargue EN CURSO de la misma fuente;
--     - hay ejecuciones POSTERIORES 'OK' de la misma fuente (salvo
--       @PermitirFueraDeOrden = 1). Revertir fuera de orden puede ser
--       incorrecto: una ejecución posterior pudo refrescar filas que esta
--       insertó (se borrarían aunque la posterior las "confirmó"), y sus 'U'
--       guardaron como FechaAnterior el sello de esta. Lo correcto es revertir
--       de la más nueva a la más vieja;
--     - la definición de la tabla destino ya no coincide con la lista blanca
--       (una columna renombrada cambiaría el hash y no se ubicaría nada; un
--       tipo de sello o de clave distinto no ubicaría las filas);
--     - los conteos de lo revertido no cuadran con lo registrado (salvo
--       @PermitirDescuadre = 1): alguien tocó la tabla después del cargue.
--
--   Cómo queda registrado: CargueFuenteEjecucion.Estado tiene un CHECK
--   ('EN CURSO','OK','ERROR','CANCELADA') y este script NO cambia el esquema.
--   Estado se deja en 'OK' (el cargue sí terminó bien) y el hecho se escribe al
--   principio de la columna Error: "REVERTIDA <fecha> por <login> (...)". Esa
--   marca es además la guarda contra revertir dos veces. La lista "últimas 48 h"
--   de la UI la sigue mostrando como Completado (solo mira Estado). Las filas de
--   CargueFuenteReversion se conservan como evidencia.
--
--   Rendimiento y bloqueo: toma un bloqueo exclusivo de la tabla destino
--   (TABLOCKX) durante la transacción para que ningún cargue ni ETL la toque a
--   mitad. El Calificador y el Creador esperan mientras tanto: correrlo fuera
--   de horario si el cargue fue grande.
--
--   Cubre las 22 fuentes del catálogo (Fase 1 + Fase 2, 2026-09-24). Casos
--   especiales que la lista blanca declara por fuente:
--     - Clave de la fila (ColumnaClave): TitularID en todas salvo
--       CamaraEmpresa, que no tiene TitularID y se ubica por NitEmpresa (el
--       motor guarda el NIT como texto en CargueFuenteReversion.TitularID).
--     - Clave como texto (TitularTexto): NuevaEps (TitularID varchar(30)).
--     - Tipo del sello (TipoSello): 'date' en todas salvo RUES, cuya
--       FechaConsulta es 'datetime'. El motor trunca ese sello al SEGUNDO
--       (GeneradorSqlCargue.LeerSello) justamente para que SelloFechaConsulta
--       (datetime2(0)) lo ubique por igualdad exacta; aquí se compara como
--       datetime, nunca convertido a date (dos cargues de RUES del mismo día
--       tienen sellos distintos). Al devolver una 'U' o reponer una 'D' de
--       RUES la fecha vuelve redondeada al segundo: FechaAnterior es
--       datetime2(0) y el GETDATE() legacy tenía milisegundos.
--     - Flag: sin flag en DatosClaro (sin vacías); con otro nombre en
--       SaviaSalud (Estado), ARLSura (EstadoEnSura, otra tabla que la EPS Sura)
--       y CamaraEmpresa (EstadoEnCamara); int en Compensados
--       (EstadoEnCompensados int NOT NULL: la vacía repuesta lleva 0 igual).
--
--   Para una fuente nueva: agregarla a @Fuentes y sus columnas de dato a
--   @Columnas EN EL MISMO ORDEN que ColumnasDato en CatalogoCargueFuentes (el
--   orden entra al hash), con su clave, su tipo de sello y su flag reales
--   (verificados con sys.columns: la guarda 50013 lo vuelve a comprobar).
--
--   SQL dinámico SEGURO: tablas y columnas salen solo de la lista blanca, pasan
--   por QUOTENAME y se verifican contra sys.columns; los valores viajan como
--   parámetros de sp_executesql.
--
--   Uso: fijar @EjecucionID, correr (ensayo), leer los conteos, y solo entonces
--   repetir con @Confirmar = 1.
--   sqlcmd -S <servidor> -d DBCallCenter -E -C -f 65001 -i revertir_cargue_fuente.sql
--   Fecha: 2026-09-24 (Fase 1); lista blanca de la Fase 2: 2026-09-24.
-- ============================================================================

USE DBCallCenter;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── Parámetros ───────────────────────────────────────────────────────────────
DECLARE @EjecucionID          bigint = NULL;   -- ← la ejecución a revertir
DECLARE @Confirmar            bit    = 0;      -- 0 = ensayo con ROLLBACK | 1 = COMMIT
DECLARE @ReponerVacias        bit    = 1;      -- reinsertar las vacías purgadas ('D')
DECLARE @PermitirFueraDeOrden bit    = 0;      -- revertir aunque haya ejecuciones posteriores OK
DECLARE @PermitirDescuadre    bit    = 0;      -- confirmar aunque los conteos no cuadren

-- ── Lista blanca: definición de las 22 fuentes ──────────────────────────────
-- Copia de CatalogoCargueFuentes (Application/Constants). El ORDEN de @Columnas
-- es el de ColumnasDato: HashContenido concatena en ese orden. Tipos verificados
-- contra sys.columns el 2026-09-24.
DECLARE @Fuentes TABLE (
    Fuente       varchar(30) NOT NULL PRIMARY KEY,   -- = CargueFuenteEjecucion.Fuente (clave del catálogo)
    Tabla        sysname     NOT NULL,
    ColumnaClave sysname     NOT NULL,   -- columna que identifica la fila (ColumnaClaveTitular)
    TitularTexto bit         NOT NULL,   -- 1 = la clave es varchar(30) en el destino (NuevaEps); 0 = bigint
    ColumnaFlag  sysname     NULL,       -- NULL = la fuente no tiene flag ni vacías (DatosClaro)
    ColumnaFecha sysname     NOT NULL,
    TipoSello    sysname     NOT NULL    -- tipo REAL de ColumnaFecha: 'date' | 'datetime' (RUES)
);
INSERT INTO @Fuentes (Fuente, Tabla, ColumnaClave, TitularTexto, ColumnaFlag, ColumnaFecha, TipoSello) VALUES
    -- Fase 1
    ('Sura',             'Sura',             'TitularID',  0, 'EstadoEnSura',            'FechaConsulta',  'date'),
    ('Cajacopi',         'Cajacopi',         'TitularID',  0, 'EstadoEnCajacopi',        'FechaConsulta',  'date'),
    ('MutualSer',        'MutualSer',        'TitularID',  0, 'EstadoEnMutualSer',       'FechaConsulta',  'date'),
    ('NuevaEps',         'NuevaEps',         'TitularID',  1, 'EstadoEnNuevaEps',        'FechaConsulta',  'date'),
    ('SIMIT',            'SIMIT',            'TitularID',  0, 'EstadoEnSIMIT',           'FechaConsulta',  'date'),
    ('DatosClaro',       'DatosClaro',       'TitularID',  0, NULL,                      'FechaInsercion', 'date'),
    -- Fase 2, grupo salud
    ('Positiva',         'Positiva',         'TitularID',  0, 'EstadoEnPositiva',        'FechaConsulta',  'date'),
    ('SaludTotal',       'SaludTotal',       'TitularID',  0, 'EstadoEnSaludTotal',      'FechaConsulta',  'date'),
    ('SOS',              'SOS',              'TitularID',  0, 'EstadoEnSOS',             'FechaConsulta',  'date'),
    ('Coosalud',         'Coosalud',         'TitularID',  0, 'EstadoEnCoosalud',        'FechaConsulta',  'date'),
    ('SaviaSalud',       'SaviaSalud',       'TitularID',  0, 'Estado',                  'FechaConsulta',  'date'),
    ('MiVacuna',         'MiVacuna',         'TitularID',  0, 'EstadoEnMiVacuna',        'FechaConsulta',  'date'),
    -- Fase 2, grupo otras
    ('Fosyga',           'Fosyga',           'TitularID',  0, 'EstadoEnFosyga',          'FechaConsulta',  'date'),
    ('RGM',              'RGM',              'TitularID',  0, 'EstadoEnRGM',             'FechaConsulta',  'date'),
    ('TigoUne',          'TigoUne',          'TitularID',  0, 'EstadoEnTigoUne',         'FechaConsulta',  'date'),
    ('Tigo',             'Tigo',             'TitularID',  0, 'EstadoEnTigo',            'FechaConsulta',  'date'),
    -- Fase 2, grupo empresas
    ('RUES',             'RUES',             'TitularID',  0, 'EstadoEnRUES',            'FechaConsulta',  'datetime'),
    ('Camara',           'Camara',           'TitularID',  0, 'EstadoEnCamara',          'FechaConsulta',  'date'),
    ('CamaraEmpresa',    'CamaraEmpresa',    'NitEmpresa', 0, 'EstadoEnCamara',          'FechaConsulta',  'date'),
    ('PositivaEmpresas', 'PositivaEmpresas', 'TitularID',  0, 'EstadoEnPositivaEmpresa', 'FechaConsulta',  'date'),
    ('ARLSura',          'ARLSura',          'TitularID',  0, 'EstadoEnSura',            'FechaConsulta',  'date'),
    ('Compensados',      'Compensados',      'TitularID',  0, 'EstadoEnCompensados',     'FechaConsulta',  'date');

DECLARE @Columnas TABLE (Fuente varchar(30) NOT NULL, Orden int NOT NULL, Columna sysname NOT NULL,
                         PRIMARY KEY (Fuente, Orden));
INSERT INTO @Columnas (Fuente, Orden, Columna) VALUES
    -- Fase 1
    ('Sura', 1, 'NumeroCelular'), ('Sura', 2, 'Email'), ('Sura', 3, 'NIT'), ('Sura', 4, 'Empresa'),
    ('Sura', 5, 'TipoAfiliado'), ('Sura', 6, 'EstadoAfiliacion'), ('Sura', 7, 'UltimaFechaConsulta'),
    ('Cajacopi', 1, 'NumeroTelefonoCelular'), ('Cajacopi', 2, 'NumeroTelefonoFijo'), ('Cajacopi', 3, 'Email'),
    ('Cajacopi', 4, 'Departamento'), ('Cajacopi', 5, 'Municipio'), ('Cajacopi', 6, 'FechaNacimiento'),
    ('MutualSer', 1, 'NumeroTelefonoCelular'), ('MutualSer', 2, 'NumeroTelefonoCelular2'),
    ('MutualSer', 3, 'NumeroTelefonoFijo'), ('MutualSer', 4, 'Email'), ('MutualSer', 5, 'Regimen'),
    ('MutualSer', 6, 'TipoAfiliado'), ('MutualSer', 7, 'Departamento'), ('MutualSer', 8, 'Municipio'),
    ('NuevaEps', 1, 'NumeroTelefono'), ('NuevaEps', 2, 'EstadoAfiliacion'), ('NuevaEps', 3, 'TipoAfiliado'),
    ('NuevaEps', 4, 'FechaNacimiento'), ('NuevaEps', 5, 'Departamento'), ('NuevaEps', 6, 'Municipio'),
    ('SIMIT', 1, 'Placa'),
    ('DatosClaro', 1, 'NumeroTelefono'), ('DatosClaro', 2, 'Email'),
    -- Fase 2, grupo salud
    ('Positiva', 1, 'NumeroTelefonoCelular'), ('Positiva', 2, 'NumeroTelefonoFijo'), ('Positiva', 3, 'Email'),
    ('Positiva', 4, 'FechaNacimiento'), ('Positiva', 5, 'Departamento'), ('Positiva', 6, 'Municipio'),
    ('SaludTotal', 1, 'NumeroTelefono'), ('SaludTotal', 2, 'Email'), ('SaludTotal', 3, 'FechaNacimiento'),
    ('SaludTotal', 4, 'EstadoAfiliacion'), ('SaludTotal', 5, 'TipoAfiliado'), ('SaludTotal', 6, 'Ciudad'),
    ('SaludTotal', 7, 'NIT'), ('SaludTotal', 8, 'Empresa'), ('SaludTotal', 9, 'Cargo'),
    ('SaludTotal', 10, 'UltimaFechaPago'), ('SaludTotal', 11, 'IngresoBase'),
    ('SOS', 1, 'NitEmpresa'), ('SOS', 2, 'RazonSocial'), ('SOS', 3, 'Telefono'), ('SOS', 4, 'Direccion'),
    ('Coosalud', 1, 'NumeroTelefono'), ('Coosalud', 2, 'Email'), ('Coosalud', 3, 'MunicipioOrigen'),
    ('Coosalud', 4, 'DepartamentoOrigen'), ('Coosalud', 5, 'Regimen'), ('Coosalud', 6, 'TipoAfiliado'),
    ('Coosalud', 7, 'EstadoAfiliacion'), ('Coosalud', 8, 'NivelSisben'), ('Coosalud', 9, 'FechaNacimiento'),
    ('SaviaSalud', 1, 'FechaDeNacimiento'), ('SaviaSalud', 2, 'TipoAfiliado'), ('SaviaSalud', 3, 'EstadoDeAfiliacion'),
    ('SaviaSalud', 4, 'Regimen'), ('SaviaSalud', 5, 'NumeroTelefono'), ('SaviaSalud', 6, 'NumeroCelular'),
    ('MiVacuna', 1, 'FechaExpedicion'), ('MiVacuna', 2, 'Celular'), ('MiVacuna', 3, 'Email'),
    -- Fase 2, grupo otras
    ('Fosyga', 1, 'Estado'), ('Fosyga', 2, 'EPS'), ('Fosyga', 3, 'Regimen'), ('Fosyga', 4, 'FechaAfiliacionEfectiva'),
    ('Fosyga', 5, 'FechaFinalizacionAfiliacion'), ('Fosyga', 6, 'TipoAfiliado'), ('Fosyga', 7, 'Departamento'),
    ('Fosyga', 8, 'Municipio'),
    ('RGM', 1, 'Marca'), ('RGM', 2, 'Modelo'), ('RGM', 3, 'Placa'),
    ('TigoUne', 1, 'NumeroTelefono'), ('TigoUne', 2, 'Email'),
    ('Tigo', 1, 'NumeroTelefono'), ('Tigo', 2, 'Email'),
    -- Fase 2, grupo empresas
    ('RUES', 1, 'NumeroMatricula'), ('RUES', 2, 'EstadoEmpresa'), ('RUES', 3, 'ActividadEconomica'),
    ('RUES', 4, 'NombreEmpresa'),
    ('Camara', 1, 'NumeroTelefono'), ('Camara', 2, 'Email'), ('Camara', 3, 'Municipio'), ('Camara', 4, 'Departamento'),
    ('CamaraEmpresa', 1, 'NumeroTelefono'), ('CamaraEmpresa', 2, 'Email'),
    ('PositivaEmpresas', 1, 'NIT'), ('PositivaEmpresas', 2, 'Empresa'), ('PositivaEmpresas', 3, 'EstadoAfiliacion'),
    ('ARLSura', 1, 'NIT'), ('ARLSura', 2, 'Empresa'),
    ('Compensados', 1, 'EPS'), ('Compensados', 2, 'UltimoPeriodoCompensado'), ('Compensados', 3, 'TipoAfiliado');

-- ── Guardas sobre la ejecución ───────────────────────────────────────────────
DECLARE @Fuente varchar(30), @Estado varchar(12), @SelloDT datetime2(0), @ErrorPrevio nvarchar(1000),
        @InsertadasReg int, @RefrescadasReg int, @PurgadasReg int, @Msg nvarchar(2048);

IF @EjecucionID IS NULL
    THROW 50001, N'Fijar @EjecucionID antes de correr el script.', 1;

SELECT @Fuente = e.Fuente, @Estado = e.Estado, @SelloDT = e.SelloFechaConsulta, @ErrorPrevio = e.Error,
       @InsertadasReg = e.Insertadas, @RefrescadasReg = e.Refrescadas, @PurgadasReg = e.VaciasPurgadas
  FROM dbo.CargueFuenteEjecucion e
 WHERE e.EjecucionID = @EjecucionID;

IF @Fuente IS NULL
BEGIN
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' no existe en dbo.CargueFuenteEjecucion.');
    THROW 50002, @Msg, 1;
END
IF @Estado <> 'OK'
BEGIN
    -- ERROR / CANCELADA / EN CURSO: la transacción del cargue se revirtió (o sigue abierta); no hay nada que deshacer.
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' está en estado ''', @Estado, N''': solo se revierte una ejecución OK.');
    THROW 50003, @Msg, 1;
END
IF @ErrorPrevio LIKE N'REVERTIDA%'
BEGIN
    SET @Msg = CONCAT(N'La ejecución ', @EjecucionID, N' ya fue revertida: ', @ErrorPrevio);
    THROW 50004, @Msg, 1;
END
IF @SelloDT IS NULL
    THROW 50005, N'La ejecución no tiene SelloFechaConsulta: no se pueden ubicar sus filas.', 1;

DECLARE @Tabla sysname, @ColClave sysname, @TitularTexto bit, @ColFlag sysname, @ColFecha sysname, @TipoSello sysname;
SELECT @Tabla = f.Tabla, @ColClave = f.ColumnaClave, @TitularTexto = f.TitularTexto, @ColFlag = f.ColumnaFlag,
       @ColFecha = f.ColumnaFecha, @TipoSello = f.TipoSello
  FROM @Fuentes f
 WHERE f.Fuente = @Fuente;
IF @Tabla IS NULL
BEGIN
    SET @Msg = CONCAT(N'La fuente ''', @Fuente, N''' no está en la lista blanca de este script: agregarla (ver cabecera) antes de revertir.');
    THROW 50006, @Msg, 1;
END

IF EXISTS (SELECT 1 FROM dbo.CargueFuenteEjecucion p WHERE p.Fuente = @Fuente AND p.Estado = 'EN CURSO')
BEGIN
    SET @Msg = CONCAT(N'Hay un cargue de ', @Fuente, N' EN CURSO. Esperar a que termine (o, si murió con un ',
                      N'reinicio, abrir la lista de cargues en la UI para que se cierre como Interrumpido).');
    THROW 50007, @Msg, 1;
END

DECLARE @Posteriores nvarchar(400) =
    (SELECT STRING_AGG(CONVERT(nvarchar(20), p.EjecucionID), N', ') WITHIN GROUP (ORDER BY p.EjecucionID)
       FROM dbo.CargueFuenteEjecucion p
      WHERE p.Fuente = @Fuente AND p.EjecucionID > @EjecucionID AND p.Estado = 'OK'
        AND ISNULL(p.Error, N'') NOT LIKE N'REVERTIDA%');
IF @Posteriores IS NOT NULL
BEGIN
    IF @PermitirFueraDeOrden = 0
    BEGIN
        SET @Msg = CONCAT(N'Hay ejecuciones posteriores OK de ', @Fuente, N' (', @Posteriores, N'). Revertirlas antes, ',
                          N'de la más nueva a la más vieja, o correr con @PermitirFueraDeOrden = 1 sabiendo el riesgo.');
        THROW 50008, @Msg, 1;
    END
    PRINT CONCAT(N'⚠ Revirtiendo FUERA DE ORDEN: hay ejecuciones posteriores OK de ', @Fuente, N' (', @Posteriores, N').');
END

-- La definición real de la tabla debe seguir siendo la de la lista blanca.
IF COL_LENGTH(N'dbo.' + @Tabla, @ColClave) IS NULL
   OR COL_LENGTH(N'dbo.' + @Tabla, @ColFecha) IS NULL
   OR (@ColFlag IS NOT NULL AND COL_LENGTH(N'dbo.' + @Tabla, @ColFlag) IS NULL)
   OR EXISTS (SELECT 1 FROM @Columnas c WHERE c.Fuente = @Fuente AND COL_LENGTH(N'dbo.' + @Tabla, c.Columna) IS NULL)
BEGIN
    SET @Msg = CONCAT(N'dbo.', @Tabla, N' ya no tiene las columnas de la lista blanca: actualizar el script antes de revertir.');
    THROW 50009, @Msg, 1;
END

-- Tipo real del sello y de la clave: con otro tipo la comparación por igualdad no ubicaría las filas
-- (un sello datetime comparado como date, o una clave varchar comparada como bigint).
DECLARE @TipoFechaReal sysname =
    (SELECT TYPE_NAME(c.user_type_id) FROM sys.columns c
      WHERE c.object_id = OBJECT_ID(N'dbo.' + @Tabla, N'U') AND c.name = @ColFecha);
DECLARE @TipoClaveReal sysname =
    (SELECT TYPE_NAME(c.user_type_id) FROM sys.columns c
      WHERE c.object_id = OBJECT_ID(N'dbo.' + @Tabla, N'U') AND c.name = @ColClave);
IF @TipoFechaReal <> @TipoSello
   OR @TipoClaveReal <> CASE WHEN @TitularTexto = 1 THEN N'varchar' ELSE N'bigint' END
BEGIN
    SET @Msg = CONCAT(N'dbo.', @Tabla, N': ', @ColFecha, N' es ', @TipoFechaReal, N' (lista blanca: ', @TipoSello, N') y ',
                      @ColClave, N' es ', @TipoClaveReal, N'. Actualizar la lista blanca antes de revertir.');
    THROW 50013, @Msg, 1;
END

-- ── Lo registrado ────────────────────────────────────────────────────────────
DECLARE @RegI int, @RegU int, @RegD int;
SELECT @RegI = ISNULL(SUM(CASE WHEN r.Accion = 'I' THEN 1 ELSE 0 END), 0),
       @RegU = ISNULL(SUM(CASE WHEN r.Accion = 'U' THEN 1 ELSE 0 END), 0),
       @RegD = ISNULL(SUM(CASE WHEN r.Accion = 'D' THEN 1 ELSE 0 END), 0)
  FROM dbo.CargueFuenteReversion r
 WHERE r.EjecucionID = @EjecucionID;

PRINT CONCAT(N'Ejecución ', @EjecucionID, N' — ', @Fuente, N' — sello ',
             CASE WHEN @TipoSello = N'datetime' THEN CONVERT(char(19), @SelloDT, 120) ELSE CONVERT(char(10), @SelloDT, 23) END);
PRINT CONCAT(N'  Registrado en CargueFuenteEjecucion: insertadas ', @InsertadasReg, N', refrescadas ', @RefrescadasReg,
             N', vacías purgadas ', @PurgadasReg);
PRINT CONCAT(N'  Filas en CargueFuenteReversion:      I ', @RegI, N', U ', @RegU, N', D ', @RegD);
-- U puede superar a Refrescadas: Refrescadas se cuenta del lado del archivo y una fila del
-- archivo refresca TODAS las filas idénticas del destino (duplicados legacy).

IF @RegI + @RegU + @RegD = 0
    THROW 50010, N'La ejecución no tiene filas en CargueFuenteReversion: no hay nada que revertir.', 1;
IF (@RegI <> ISNULL(@InsertadasReg, -1) OR @RegD <> ISNULL(@PurgadasReg, -1) OR @RegU < ISNULL(@RefrescadasReg, 0))
   AND @PermitirDescuadre = 0
    THROW 50011, N'CargueFuenteReversion no cuadra con los conteos de la ejecución. Revisar a mano (o @PermitirDescuadre = 1).', 1;

-- ── SQL dinámico desde la lista blanca ──────────────────────────────────────
DECLARE @T nvarchar(300) = QUOTENAME(N'dbo') + N'.' + QUOTENAME(@Tabla);
DECLARE @F nvarchar(300) = QUOTENAME(@ColFecha);
DECLARE @K nvarchar(300) = QUOTENAME(@ColClave);
-- Tipo SQL del sello: el parámetro @Sello y las fechas devueltas van con el tipo REAL del destino.
DECLARE @TipoSelloSql nvarchar(20) = CASE WHEN @TipoSello = N'datetime' THEN N'datetime' ELSE N'date' END;

-- Hash del contenido: MISMA expresión que GeneradorSqlCargue.HashContenido
--   HASHBYTES('SHA2_256', CONCAT_WS(N'|', ISNULL(CONVERT(nvarchar(400), s.[Col]), N'~'), ...))
-- con una sola columna (SIMIT) va sin CONCAT_WS, igual que el generador.
DECLARE @H nvarchar(max), @NCols int;
SELECT @NCols = COUNT(*),
       @H = STRING_AGG(CONVERT(nvarchar(max), N'ISNULL(CONVERT(nvarchar(400), s.' + QUOTENAME(c.Columna) + N'), N''~'')'), N', ')
                WITHIN GROUP (ORDER BY c.Orden)
  FROM @Columnas c
 WHERE c.Fuente = @Fuente;
SET @H = N'HASHBYTES(''SHA2_256'', ' + CASE WHEN @NCols = 1 THEN @H ELSE N'CONCAT_WS(N''|'', ' + @H + N')' END + N')';

-- Clave de CargueFuenteReversion (varchar) contra el destino: la conversión va del lado de r
-- para que la clave del destino use su índice (IX_<Tabla>_TitularID_...). CamaraEmpresa es un
-- heap sin índices: su NitEmpresa se compara igual, por valor bigint.
DECLARE @MismoTitular nvarchar(300) = CASE WHEN @TitularTexto = 1
    THEN N's.' + @K + N' = r.TitularID'
    ELSE N's.' + @K + N' = CONVERT(bigint, r.TitularID)' END;
DECLARE @TitularDesdeR nvarchar(100) = CASE WHEN @TitularTexto = 1 THEN N'r.TitularID' ELSE N'CONVERT(bigint, r.TitularID)' END;

-- 'I': borrar, por (clave, hash), a lo sumo tantas filas como registró la ejecución.
-- DELETE sobre una CTE de una sola tabla (el ROW_NUMBER numera filas idénticas; el destino es heap).
DECLARE @SqlI nvarchar(max) = N'
;WITH d AS (
    SELECT CONVERT(varchar(30), s.' + @K + N') AS _T, ' + @H + N' AS _H,
           ROW_NUMBER() OVER (PARTITION BY s.' + @K + N', ' + @H + N' ORDER BY (SELECT NULL)) AS _rn
      FROM ' + @T + N' AS s
     WHERE s.' + @F + N' = @Sello
       AND EXISTS (SELECT 1 FROM dbo.CargueFuenteReversion r
                    WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''I'' AND ' + @MismoTitular + N'))
DELETE FROM d
 WHERE d._rn <= (SELECT COUNT(*) FROM dbo.CargueFuenteReversion r
                  WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''I''
                    AND r.TitularID = d._T AND r.HashContenido = d._H);
SET @n = @@ROWCOUNT;';

-- 'U': devolver la fecha anterior. Las filas de un mismo (clave, hash) son idénticas (mismo
-- contenido, misma fecha de sello): se emparejan por número de fila y da igual cuál recibe cuál.
DECLARE @SqlU nvarchar(max) = N'
;WITH d AS (
    SELECT s.' + @F + N' AS _Fecha, CONVERT(varchar(30), s.' + @K + N') AS _T, ' + @H + N' AS _H,
           ROW_NUMBER() OVER (PARTITION BY s.' + @K + N', ' + @H + N' ORDER BY (SELECT NULL)) AS _rn
      FROM ' + @T + N' AS s
     WHERE s.' + @F + N' = @Sello
       AND EXISTS (SELECT 1 FROM dbo.CargueFuenteReversion r
                    WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''U'' AND ' + @MismoTitular + N')),
u AS (
    SELECT r.TitularID, r.HashContenido, r.FechaAnterior,
           ROW_NUMBER() OVER (PARTITION BY r.TitularID, r.HashContenido ORDER BY r.FechaAnterior) AS _rn
      FROM dbo.CargueFuenteReversion r
     WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''U'')
UPDATE d SET d._Fecha = CONVERT(' + @TipoSelloSql + N', u.FechaAnterior)
  FROM d
  JOIN u ON u.TitularID = d._T AND u.HashContenido = d._H AND u._rn = d._rn;
SET @n = @@ROWCOUNT;';

-- 'D': reinsertar la vacía (clave + flag 0 + fecha original; el resto NULL, ver cabecera).
-- El 0 vale para flags bit y para el int de Compensados.
DECLARE @SqlD nvarchar(max) = CASE WHEN @ColFlag IS NULL THEN NULL ELSE N'
INSERT INTO ' + @T + N' (' + @K + N', ' + QUOTENAME(@ColFlag) + N', ' + @F + N')
SELECT ' + @TitularDesdeR + N', 0, CONVERT(' + @TipoSelloSql + N', r.FechaAnterior)
  FROM dbo.CargueFuenteReversion r
 WHERE r.EjecucionID = @EjecucionID AND r.Accion = ''D'';
SET @n = @@ROWCOUNT;' END;

DECLARE @Bloqueo nvarchar(max) = N'SELECT TOP (0) 1 FROM ' + @T + N' WITH (TABLOCKX, HOLDLOCK);';
-- @Sello se declara con el tipo del destino; el valor viaja como datetime2(0) y sp_executesql lo
-- convierte: a date se queda con el día; a datetime, exacto (ya viene truncado al segundo).
DECLARE @Params nvarchar(200) = N'@EjecucionID bigint, @Sello ' + @TipoSelloSql + N', @n int OUTPUT';
DECLARE @n int, @Borradas int = 0, @Restauradas int = 0, @Repuestas int = 0;

-- ── Transacción ──────────────────────────────────────────────────────────────
BEGIN TRY
    BEGIN TRANSACTION;

    EXEC sys.sp_executesql @Bloqueo;

    EXEC sys.sp_executesql @SqlI, @Params, @EjecucionID = @EjecucionID, @Sello = @SelloDT, @n = @n OUTPUT;
    SET @Borradas = @n;

    EXEC sys.sp_executesql @SqlU, @Params, @EjecucionID = @EjecucionID, @Sello = @SelloDT, @n = @n OUTPUT;
    SET @Restauradas = @n;

    IF @SqlD IS NOT NULL AND @ReponerVacias = 1
    BEGIN
        EXEC sys.sp_executesql @SqlD, @Params, @EjecucionID = @EjecucionID, @Sello = @SelloDT, @n = @n OUTPUT;
        SET @Repuestas = @n;
    END

    PRINT CONCAT(N'  Borradas (I): ', @Borradas, N' de ', @RegI,
                 N' | Fecha devuelta (U): ', @Restauradas, N' de ', @RegU,
                 N' | Vacías repuestas (D): ', @Repuestas, N' de ', CASE WHEN @ReponerVacias = 1 THEN @RegD ELSE 0 END);

    -- Si no cuadra, alguien tocó la tabla después del cargue (otro cargue, un ETL, un UPDATE a mano).
    IF (@Borradas <> @RegI OR @Restauradas <> @RegU
        OR @Repuestas <> CASE WHEN @ReponerVacias = 1 AND @SqlD IS NOT NULL THEN @RegD ELSE 0 END)
       AND @PermitirDescuadre = 0
        THROW 50012, N'Lo revertido no cuadra con CargueFuenteReversion: se deshizo todo. Revisar qué tocó la tabla después del cargue.', 1;

    UPDATE dbo.CargueFuenteEjecucion
       SET Error = LEFT(CONCAT(N'REVERTIDA ', CONVERT(char(19), SYSDATETIME(), 120), N' por ', SUSER_SNAME(),
                               N' (revertir_cargue_fuente.sql): I ', @Borradas, N', U ', @Restauradas, N', D ', @Repuestas,
                               CASE WHEN @Posteriores IS NOT NULL THEN N', FUERA DE ORDEN' ELSE N'' END,
                               CASE WHEN @ErrorPrevio IS NOT NULL THEN N' | ' + @ErrorPrevio ELSE N'' END), 1000)
     WHERE EjecucionID = @EjecucionID AND Estado = 'OK';

    IF @Confirmar = 1
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'COMMIT: ejecución revertida. Reciclar el AppPool (o esperar la 1 p. m.) para limpiar la caché del Calificador.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'ENSAYO: se revirtió todo (ROLLBACK). Si los conteos son los esperados, repetir con @Confirmar = 1.';
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

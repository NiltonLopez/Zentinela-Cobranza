-- =====================================================================
-- Migración: 2026-07-11 — Crear dbo.fn_BaseMadreParaTitulares_CarteraChica
-- =====================================================================
-- Motivo: el Calificador de Cartera se quedaba "cargando" (varios minutos,
--   a veces sin terminar) en carteras CHICAS como "Prima y Nuevagarantia —
--   Andres" (CarteraID 25, LiderID 7, 3.135 titulares), aunque carteras
--   grandes (100k+ titulares) funcionaban bien. Contraintuitivo: a menos
--   titulares, más lento.
--
--   Diagnóstico (plan de ejecución real, SET STATISTICS XML ON): para
--   6 de las ~15 fuentes que cruza dbo.fn_BaseMadreParaTitulares (Camara,
--   DatosClaro, Fosyga, MutualSer, Positiva, PositivaEmpresas), el
--   optimizador arma -SOLO EN CARTERAS CHICAS- un plan de Nested Loops que
--   reescanea completo el TVP @Titulares una vez por cada fila del lado
--   externo: para 3.135 titulares eso es 3.135² = 9.828.225 reescaneos
--   (confirmado exacto en el plan real), que después escala hacia la
--   tabla fuente (12.1M filas leídas de Fosyga, 11.6M de Positiva, etc.
--   para solo 3.135 titulares reales). CXSYNC_PORT en sys.dm_exec_requests
--   era el síntoma (hilos paralelos sincronizándose), no la causa.
--
--   Medido (Cartera 25/Líder 7, 3.135 titulares, columnas completas):
--     - Función original (sin cambios):                    148.766 ms
--     - Misma función forzando HASH JOIN en esas 6 fuentes:   4.782 ms
--
--   HASH JOIN construye una tabla hash sobre @Titulares una sola vez y
--   sondea cada fuente una vez por titular — elimina el reescaneo
--   cuadrático sin importar por qué el optimizador lo elegía.
--
--   OJO: forzar HASH JOIN de forma universal ROMPE las carteras grandes
--   (medido: Cartera 22/Líder 6, 116.997 titulares, función original sin
--   cambios = 3.300 ms; misma función con HASH JOIN forzado = 64.353 ms,
--   ~19x más lento). Por eso esto NO reemplaza a fn_BaseMadreParaTitulares
--   — es una función hermana que CalificadorCarteraService.CalificarAsync
--   elige en tiempo de ejecución según la cantidad de titulares de la
--   cartera (ver UmbralCarteraChica en ese archivo). Duplicar el cuerpo de
--   la función es preferible a intentar una sola función con lógica
--   condicional: las TVF inline (RETURNS TABLE AS RETURN) no admiten
--   IF/lógica condicional, y pasar a una función multi-statement o a un
--   procedimiento perdería la inlineabilidad que hace rápida a la función
--   actual para carteras grandes.
--
--   Umbral elegido (20.000 titulares): conservador, no es el cruce exacto.
--   Medido en el camino "sin hint" (función original): 3.135 titulares
--   tarda 148.8s, 10.000 titulares tarda 17+ minutos sin terminar (sesión
--   matada manualmente — más lento todavía, no una mejora lineal), y
--   116.997 titulares tarda 3.3s. El cruce real está en algún punto entre
--   10.000 y 116.997, pero acotarlo con más precisión implica correr la
--   consulta completa repetidas veces contra el servidor compartido de
--   producción (una sola corrida en la "zona mala" ya tardó 17+ min) — no
--   se justifica el riesgo. Sí se confirmó que el camino HASH JOIN se
--   mantiene rápido cerca del límite elegido: 20.000 titulares con HASH
--   JOIN = 14.263 ms. Si en el futuro se detectan carteras entre ~20k y
--   ~100k titulares con el mismo síntoma, hay que recalibrar el umbral (o
--   investigar la causa raíz de por qué el optimizador falla en ese rango,
--   ver TODO abajo).
--
--   TODO sin resolver: por qué el optimizador elige Nested Loops en vez de
--   Hash Match para carteras chicas en primer lugar (posible desactualización
--   de estadísticas en Fosyga/Positiva/Camara/DatosClaro/MutualSer/
--   PositivaEmpresas, o un umbral de costo interno de SQL Server). Si se
--   corrige esa causa raíz, esta función hermana y el branching en
--   CalificadorCarteraService dejarían de hacer falta.
--
-- Consumidor único previsto: CalificadorCarteraService.CalificarAsync,
--   cuando titulares.Rows.Count <= UmbralCarteraChica (20.000).
--
-- Idempotente: DROP FUNCTION + CREATE, igual que la migración de la que
--   depende (2026-07-06_fn_BaseMadreParaTitulares.sql — requiere
--   dbo.TitularIDList, no crea tipos nuevos).
--
-- BD: DBCallCenter (10.0.0.89,1433). Aplicar como sa o equivalente.
-- =====================================================================

USE DBCallCenter;
GO

IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares_CarteraChica', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica;
GO

-- Copia exacta de dbo.fn_BaseMadreParaTitulares (ver
-- 2026-07-06_fn_BaseMadreParaTitulares.sql), salvo INNER HASH JOIN forzado
-- en las 6 CTEs que explotan en carteras chicas: Camara, DatosClaro,
-- Fosyga, MutualSer, Positiva, PositivaEmpresas. Las otras ~9 CTEs
-- (ARLSura, Cajacopi, MiVacuna, NuevaEps, RGM, RUES, SaludTotal, SIMIT,
-- SOS, Compensados) ya elegían un plan eficiente por su cuenta — no se
-- tocan para no introducir un cambio de comportamiento innecesario.
CREATE FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica
(
    @CarteraID int,
    @Titulares dbo.TitularIDList READONLY
)
RETURNS TABLE
AS
RETURN
(
    WITH
    UltimoARL              AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.ARLSura          s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoCajacopi         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Cajacopi         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoCamara           AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Camara           s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoDatosClaro       AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaInsercion DESC) AS rn FROM dbo.DatosClaro       s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoFosyga           AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Fosyga           s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoMiVacuna         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.MiVacuna         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoMutualSer        AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.MutualSer        s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoNuevaEps         AS (SELECT s2.TitularID, s2.NumeroTelefono, s2.EstadoAfiliacion, s2.TipoAfiliado, s2.FechaNacimiento, s2.Departamento, s2.Municipio, s2.EstadoEnNuevaEps, s2.FechaConsulta,
                                      ROW_NUMBER() OVER (PARTITION BY s2.TitularID ORDER BY s2.FechaConsulta DESC) AS rn
                               FROM (SELECT TRY_CAST(TitularID AS BIGINT) AS TitularID, NumeroTelefono, EstadoAfiliacion, TipoAfiliado, FechaNacimiento, Departamento, Municipio, EstadoEnNuevaEps, FechaConsulta FROM dbo.NuevaEps) s2
                               INNER JOIN @Titulares t ON t.TitularID = s2.TitularID),
    UltimoPositiva         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Positiva         s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoPositivaEmpresas AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.PositivaEmpresas s INNER HASH JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoRGM              AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.RGM              s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoRUES             AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.RUES             s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoSaludTotal       AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.SaludTotal       s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoSIMIT            AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.SIMIT            s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoSOS              AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.SOS              s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoTigo             AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Tigo             s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoCompensados      AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Compensados      s INNER JOIN @Titulares t ON t.TitularID = s.TitularID)
    SELECT
        CAST(Ti.NumeroDocumento AS VARCHAR(20))                         AS [Numero Documento],
        ISNULL(Ti.Nombre, 0)                                            AS [Nombre],
        ISNULL(CAST(UST.SaldoTotal AS VARCHAR(30)), 0)                  AS [Saldo],
        ISNULL(CONVERT(VARCHAR(10), UST.FechaUltimaGestion, 103), 0)    AS [Ultima Fecha Gestion],
        ISNULL(CONVERT(VARCHAR(10), UST.FechaPromesa, 103), 0)          AS [Fecha Promesa],
        ISNULL(CAST(UST.ValorPromesa AS VARCHAR(30)), 0)                AS [Valor Promesa],
        ISNULL(UST.MejorPerfil, 0)                                      AS [Mejor Perfil],
        ISNULL(UST.UltimoPerfil, 0)                                     AS [Ultimo Perfil],
        ISNULL(CAST(UST.Rotacion AS VARCHAR(10)), 0)                    AS [Rotacion],
        ISNULL(UST.NumeroMarcado, 0)                                    AS [Ultimo Numero Marcado en ControlNext],
        ISNULL(F.Estado, 0)                                             AS [Estado en Fosyga],
        ISNULL(F.EPS, 0)                                                AS [EPS],
        ISNULL(F.Regimen, 0)                                            AS [Regimen],
        ISNULL(F.TipoAfiliado, 0)                                       AS [Tipo Afiliado],
        ISNULL(F.Departamento, 0)                                       AS [Departamento],
        ISNULL(F.Municipio, 0)                                          AS [Municipio],
        ISNULL(F.FechaConsulta, '1900-01-01')                           AS [Ultima Consulta en Fosyga],
        ISNULL(CO.EPS, 0)                                               AS [EPS en Compensados],
        ISNULL(CO.UltimoPeriodoCompensado, '1900-01-01')                AS [Ultimo Periodo Compensado],
        ISNULL(CO.TipoAfiliado, 0)                                      AS [Tipo Afiliado Compensados],
        ISNULL(CO.FechaConsulta, '1900-01-01')                          AS [Ultima Consulta en Compensados],
        ISNULL(DATEDIFF(YEAR, DBR.FechaNacimiento, GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, DBR.FechaNacimiento, GETDATE()), DBR.FechaNacimiento) > GETDATE() THEN 1 ELSE 0 END, 0) AS [Edad Registraduria],
        ISNULL(DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()), CP.FechaNacimiento) > GETDATE() THEN 1 ELSE 0 END, 0) AS [Edad CajaCopi],
        ISNULL(DATEDIFF(YEAR, P.FechaNacimiento,  GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, P.FechaNacimiento,  GETDATE()), P.FechaNacimiento)  > GETDATE() THEN 1 ELSE 0 END, 0)  AS [Edad Positiva],
        ISNULL(CP.NumeroTelefonoCelular, 0) AS [Celular Cajacopi],     ISNULL(CP.Email, 0) AS [Email CajaCopi],
        ISNULL(C.NumeroTelefono, 0)         AS [Celular Camara],       ISNULL(C.Email, 0)  AS [Email Camara],
        ISNULL(ST.NumeroTelefono, 0)        AS [Celular Salud Total],  ISNULL(ST.Email, 0) AS [Email Salud Total],
        ISNULL(DC.NumeroTelefono, 0)        AS [Celular Claro],        ISNULL(DC.Email, 0) AS [Email Claro],
        ISNULL(DC.FechaInsercion, '1900-01-01') AS [Ultima Insercion Claro],
        ISNULL(MV.Celular, 0)               AS [Celular MiVacuna],
        ISNULL(MV.Email, 0)                 AS [Email MiVacuna],
        ISNULL(MV.FechaExpedicion, '1900-01-01') AS [Fecha Expedicion MiVacuna],
        ISNULL(CAST(MV.EstadoEnMiVacuna AS VARCHAR(1)), 0) AS [Estado MiVacuna],
        ISNULL(MS.NumeroTelefonoCelular, 0)  AS [Celular MutualSer],
        ISNULL(MS.NumeroTelefonoCelular2, 0) AS [Celular 2 MutualSer],
        ISNULL(MS.NumeroTelefonoFijo, 0)     AS [Telefono Fijo MutualSer],
        ISNULL(MS.Email, 0)                  AS [Email MutualSer],
        ISNULL(MS.Regimen, 0)                AS [Regimen MutualSer],
        ISNULL(MS.TipoAfiliado, 0)           AS [Tipo Afiliado MutualSer],
        ISNULL(MS.Departamento, 0)           AS [Departamento MutualSer],
        ISNULL(MS.Municipio, 0)              AS [Municipio MutualSer],
        ISNULL(CAST(MS.EstadoEnMutualSer AS VARCHAR(1)), 0) AS [Estado MutualSer],
        ISNULL(MS.FechaConsulta, '1900-01-01') AS [Ultima Consulta MutualSer],
        ISNULL(NE.NumeroTelefono, 0)         AS [Celular NuevaEps],
        ISNULL(NE.EstadoAfiliacion, 0)       AS [Estado Afiliacion NuevaEps],
        ISNULL(NE.TipoAfiliado, 0)           AS [Tipo Afiliado NuevaEps],
        ISNULL(NE.FechaNacimiento, '1900-01-01') AS [Fecha Nacimiento NuevaEps],
        ISNULL(NE.Departamento, 0)           AS [Departamento NuevaEps],
        ISNULL(NE.Municipio, 0)              AS [Municipio NuevaEps],
        ISNULL(CAST(NE.EstadoEnNuevaEps AS VARCHAR(1)), 0) AS [Estado NuevaEps],
        ISNULL(NE.FechaConsulta, '1900-01-01') AS [Ultima Consulta NuevaEps],
        ISNULL(P.NumeroTelefonoCelular, 0)  AS [Celular Positiva],     ISNULL(P.Email, 0)  AS [Email Positiva],
        ISNULL(T.NumeroTelefono, 0)         AS [Celular Tigo],         ISNULL(T.Email, 0)  AS [Email Tigo],
        ISNULL(SO.Telefono, 0)              AS [Celular SOS],
        ISNULL(A.NIT, 0)                    AS [NIT Sura],             ISNULL(A.Empresa, 0)   AS [Empresa Sura],
        ISNULL(PE.NIT, 0)                   AS [NIT Positiva],         ISNULL(PE.Empresa, 0)  AS [Empresa Positiva],
        ISNULL(ST.NIT, 0)                   AS [NIT Salud Total],      ISNULL(ST.Empresa, 0)  AS [Empresa Salud Total],
        ISNULL(SO.NitEmpresa, 0)            AS [Nit Sos],              ISNULL(SO.RazonSocial, 0) AS [Empresa SOS],
        ISNULL(RU.NombreEmpresa, 0)         AS [Empresa RUES],
        ISNULL(S.Placa, 0)                  AS [PLACA SIMIT],
        ISNULL(RG.Marca, 0)                 AS [Marca],
        ISNULL(RG.Modelo, 0)                AS [Modelo],
        ISNULL(RG.Placa, 0)                 AS [Placa]
    FROM @Titulares TC
    INNER JOIN dbo.Titular Ti                  ON Ti.NumeroDocumento = TC.TitularID
    LEFT  JOIN dbo.UltimoEstadoTitular UST     ON UST.TitularID = TC.TitularID AND UST.CarteraID = @CarteraID
    LEFT  JOIN DBRegistraduria.ani.Persona DBR ON DBR.NumeroDocumento = Ti.NumeroDocumento
    LEFT  JOIN UltimoARL              A  ON TC.TitularID = A.TitularID  AND A.rn  = 1
    LEFT  JOIN UltimoCajacopi         CP ON TC.TitularID = CP.TitularID AND CP.rn = 1
    LEFT  JOIN UltimoCamara           C  ON TC.TitularID = C.TitularID  AND C.rn  = 1
    LEFT  JOIN UltimoDatosClaro       DC ON TC.TitularID = DC.TitularID AND DC.rn = 1
    LEFT  JOIN UltimoFosyga           F  ON TC.TitularID = F.TitularID  AND F.rn  = 1
    LEFT  JOIN UltimoMiVacuna         MV ON TC.TitularID = MV.TitularID AND MV.rn = 1
    LEFT  JOIN UltimoMutualSer        MS ON TC.TitularID = MS.TitularID AND MS.rn = 1
    LEFT  JOIN UltimoNuevaEps         NE ON TC.TitularID = NE.TitularID AND NE.rn = 1
    LEFT  JOIN UltimoPositiva         P  ON TC.TitularID = P.TitularID  AND P.rn  = 1
    LEFT  JOIN UltimoPositivaEmpresas PE ON TC.TitularID = PE.TitularID AND PE.rn = 1
    LEFT  JOIN UltimoRGM              RG ON TC.TitularID = RG.TitularID AND RG.rn = 1
    LEFT  JOIN UltimoRUES             RU ON TC.TitularID = RU.TitularID AND RU.rn = 1
    LEFT  JOIN UltimoSaludTotal       ST ON TC.TitularID = ST.TitularID AND ST.rn = 1
    LEFT  JOIN UltimoSIMIT            S  ON TC.TitularID = S.TitularID  AND S.rn  = 1
    LEFT  JOIN UltimoSOS              SO ON TC.TitularID = SO.TitularID AND SO.rn = 1
    LEFT  JOIN UltimoTigo             T  ON TC.TitularID = T.TitularID  AND T.rn  = 1
    LEFT  JOIN UltimoCompensados      CO ON TC.TitularID = CO.TitularID AND CO.rn = 1
);
GO

-- Smoke test (ejecutar manualmente tras aplicar):
--   DECLARE @t dbo.TitularIDList;
--   INSERT INTO @t(TitularID) SELECT TitularID FROM dbo.Titular_Cartera WHERE CarteraID = 25 AND LiderID = 7;
--   SELECT TOP 5 * FROM dbo.fn_BaseMadreParaTitulares_CarteraChica(25, @t) OPTION (RECOMPILE);

-- ─────────────────────────────────────────────────────────────────────
-- ROLLBACK (ejecutar manualmente si se necesita revertir):
--
--   USE DBCallCenter;
--   GO
--   IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares_CarteraChica', 'IF') IS NOT NULL
--       DROP FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica;
--   GO
--
-- Tras el rollback, revertir también CalificadorCarteraService.CalificarAsync
-- para que siempre use dbo.fn_BaseMadreParaTitulares (quitar el branching
-- por UmbralCarteraChica), o las carteras chicas volverán a colgarse.
-- ─────────────────────────────────────────────────────────────────────

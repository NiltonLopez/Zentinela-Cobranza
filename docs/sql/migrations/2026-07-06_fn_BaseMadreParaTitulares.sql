-- =====================================================================
-- Migración: 2026-07-06 — Crear dbo.TitularIDList (TVP) + dbo.fn_BaseMadreParaTitulares
-- =====================================================================
-- Motivo: dbo.fn_BaseMadrePorCartera reevalúa la CTE TitularesCartera 18
--   veces por llamada (una por cada CTE Ultimo* + el FROM principal) porque
--   SQL Server no materializa CTEs entre referencias. Confirmado con el plan
--   de ejecución real: Titular_Cartera se escanea 18 veces, dominando ~16.8s
--   de ~25s totales en una cartera de 147k filas (Cartera 22 / Líder 6).
--
--   Esta función recibe la lista de TitularID YA CALCULADA por el llamador
--   (una sola query previa, ~0.7s) como TVP de solo lectura, y cada CTE
--   Ultimo* la usa vía INNER JOIN en vez de WHERE TitularID IN (SELECT ...).
--   Medido: paso de materialización 16.8s -> ~1.8s (~7x) para el mismo caso.
--
--   Usa @CarteraID (parámetro escalar) para el predicado de JOIN con
--   UltimoEstadoTitular (UST.CarteraID) — evita cualquier JOIN de vuelta a
--   Titular_Cartera dentro de la función.
--
-- ¡¡¡ OPTION (RECOMPILE) ES OBLIGATORIO EN TODO CALLER, NO OPCIONAL !!!
--   Confirmado con datos reales: SIN RECOMPILE, el optimizador asume que el
--   TVP @Titulares trae ~1 fila (comportamiento estándar de SQL Server para
--   table-valued parameters, sin importar cuántas filas traiga en realidad),
--   arma un plan de nested loops, y la consulta NUNCA termina para una
--   cartera de tamaño real (sesión de prueba matada tras 6+ min sin avanzar,
--   147k filas). CON RECOMPILE, el optimizador mide el tamaño real de
--   @Titulares en cada ejecución y arma un plan correcto (medido: 3.3s para
--   el mismo caso, con las ~50 columnas completas). Cualquier consulta nueva
--   contra fn_BaseMadreParaTitulares DEBE incluir OPTION (RECOMPILE).
--
-- Consumidor único previsto: CalificadorCarteraService.CalificarAsync
--   (SqlAgregados). NO reemplaza a dbo.fn_BaseMadrePorCartera, que sigue
--   siendo usada por BaseMadreRepository (Creador Zentinela) y por
--   CalificadorCarteraService.EscribirLocalizacionCsvAsync (descarga CSV
--   por celda, ADR-019, fuera de alcance de esta migración).
--
-- Columnas: mismas ~50 columnas y alias que fn_BaseMadrePorCartera, salvo
--   que se omiten CarteraID/LiderID (SqlAgregados no las lee hoy) — así no
--   hace falta un JOIN de vuelta a Titular_Cartera para producirlas.
--
-- Idempotente: DROP FUNCTION + DROP TYPE + CREATE. Orden obligatorio: un
--   TYPE no se puede eliminar mientras una función lo referencie en su
--   firma, así que DROP FUNCTION siempre va antes que DROP TYPE, tanto al
--   aplicar como al revertir esta migración.
--
-- BD: DBCallCenter (10.0.0.89,1433). Aplicar como sa o equivalente.
-- =====================================================================

USE DBCallCenter;
GO

IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_BaseMadreParaTitulares;
GO

IF TYPE_ID('dbo.TitularIDList') IS NOT NULL
    DROP TYPE dbo.TitularIDList;
GO

-- Sin PRIMARY KEY: Titular_Cartera puede tener más de una fila para el mismo
-- TitularID dentro de la misma cartera (histórico de reasignación entre
-- líderes) — confirmado con datos reales: Cartera 22 tiene 657.983 filas
-- pero solo 547.174 TitularID distintos (110.809 titulares con más de un
-- líder histórico). El caso "Cartera Completo" (@LiderID = NULL) trae esas
-- filas duplicadas tal cual, igual que fn_BaseMadrePorCartera hoy (que
-- tampoco deduplica) — una PRIMARY KEY aquí rompería con "Violación de
-- restricción" apenas se intentara cargar una cartera con este patrón.
CREATE TYPE dbo.TitularIDList AS TABLE
(
    TitularID bigint NOT NULL
);
GO

CREATE FUNCTION dbo.fn_BaseMadreParaTitulares
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
    UltimoCamara           AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Camara           s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoDatosClaro       AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaInsercion DESC) AS rn FROM dbo.DatosClaro       s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoFosyga           AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Fosyga           s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoMiVacuna         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.MiVacuna         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoMutualSer        AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.MutualSer        s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoNuevaEps         AS (SELECT s2.TitularID, s2.NumeroTelefono, s2.EstadoAfiliacion, s2.TipoAfiliado, s2.FechaNacimiento, s2.Departamento, s2.Municipio, s2.EstadoEnNuevaEps, s2.FechaConsulta,
                                      ROW_NUMBER() OVER (PARTITION BY s2.TitularID ORDER BY s2.FechaConsulta DESC) AS rn
                               FROM (SELECT TRY_CAST(TitularID AS BIGINT) AS TitularID, NumeroTelefono, EstadoAfiliacion, TipoAfiliado, FechaNacimiento, Departamento, Municipio, EstadoEnNuevaEps, FechaConsulta FROM dbo.NuevaEps) s2
                               INNER JOIN @Titulares t ON t.TitularID = s2.TitularID),
    UltimoPositiva         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Positiva         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoPositivaEmpresas AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.PositivaEmpresas s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
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

-- Smoke test (ejecutar manualmente tras aplicar) — OJO: siempre con
-- OPTION (RECOMPILE), ver advertencia arriba:
--   DECLARE @t dbo.TitularIDList;
--   INSERT INTO @t(TitularID) SELECT TitularID FROM dbo.Titular_Cartera WHERE CarteraID = 22 AND LiderID = 6;
--   SELECT TOP 5 * FROM dbo.fn_BaseMadreParaTitulares(22, @t) OPTION (RECOMPILE);

-- ─────────────────────────────────────────────────────────────────────
-- ROLLBACK (ejecutar manualmente si se necesita revertir):
--
--   USE DBCallCenter;
--   GO
--   -- Orden obligatorio: función primero, tipo después (un TYPE no puede
--   -- DROPearse mientras exista una función cuya firma lo referencia).
--   IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares', 'IF') IS NOT NULL
--       DROP FUNCTION dbo.fn_BaseMadreParaTitulares;
--   GO
--   IF TYPE_ID('dbo.TitularIDList') IS NOT NULL
--       DROP TYPE dbo.TitularIDList;
--   GO
--
-- Tras el rollback, revertir también CalificadorCarteraService.CalificarAsync
-- a la versión que llama dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID),
-- o la llamada fallará (objeto no encontrado).
--
-- RE-APLICAR TRAS UN AJUSTE: este script ya es idempotente (los DROP del
-- inicio cubren re-ejecuciones). Si CREATE TYPE falla con "cannot drop type
-- because it is being referenced", esperar a que terminen las conexiones
-- en curso (o reciclar el pool de conexiones de la app) antes de reintentar.
-- ─────────────────────────────────────────────────────────────────────

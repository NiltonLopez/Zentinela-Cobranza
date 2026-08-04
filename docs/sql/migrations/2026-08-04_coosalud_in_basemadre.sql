-- ============================================================================
-- Fuente Coosalud -> vw_BaseMadre + fn_BaseMadrePorCartera
--                    + fn_BaseMadreParaTitulares (+_CarteraChica)
--   Fuente 1:1 tipo EPS (patron NuevaEps ampliado). Se agregan 11 columnas a la
--   vista y a los TRES iTVF: [Celular Coosalud], [Email Coosalud],
--   [Regimen Coosalud], [Tipo Afiliado Coosalud], [Estado Afiliacion Coosalud],
--   [Nivel Sisben Coosalud], [Departamento Coosalud], [Municipio Coosalud],
--   [Fecha Nacimiento Coosalud], [Estado Coosalud], [Ultima Consulta Coosalud].
--
--   OJO: a la fecha de esta migracion la tabla dbo.Coosalud esta VACIA (0 filas);
--   el cableado queda listo para el primer cargue. Columnas origen:
--   TitularID bigint, NumeroTelefono varchar(10), Email varchar(255),
--   MunicipioOrigen/DepartamentoOrigen/Regimen/TipoAfiliado/EstadoAfiliacion/
--   NivelSisben varchar(50), FechaNacimiento date, EstadoEnCoosalud bit,
--   FechaConsulta date.
--
--   Recordatorio critico: la vista se usa solo para el shape de EF; el codigo
--   productivo consume los iTVF. Los CUATRO objetos se actualizan aqui juntos.
--   Idempotente. Fecha: 2026-08-04.
-- ============================================================================

-- 1) Indice de soporte (tabla heap)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Coosalud_TitularID_FechaConsulta')
    CREATE INDEX IX_Coosalud_TitularID_FechaConsulta
        ON dbo.Coosalud (TitularID, FechaConsulta DESC);
GO

-- 2) ALTER VIEW dbo.vw_BaseMadre -- agrega CTE UltimoCoosalud + 11 columnas + JOIN
ALTER VIEW dbo.vw_BaseMadre
AS
WITH
UltimoARL AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM ARLSura),
UltimoCajacopi AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Cajacopi),
UltimoCamara AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Camara),
UltimoDatosClaro AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaInsercion DESC) AS rn FROM DatosClaro),
UltimoFosyga AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Fosyga),
UltimoMiVacuna AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM MiVacuna),
UltimoMutualSer AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM MutualSer),
UltimoNuevaEps AS (SELECT TRY_CAST(TitularID AS BIGINT) AS TitularID, NumeroTelefono, EstadoAfiliacion, TipoAfiliado, FechaNacimiento, Departamento, Municipio, EstadoEnNuevaEps, FechaConsulta, ROW_NUMBER() OVER (PARTITION BY TRY_CAST(TitularID AS BIGINT) ORDER BY FechaConsulta DESC) AS rn FROM NuevaEps),
UltimoPositiva AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Positiva),
UltimoPositivaEmpresas AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM PositivaEmpresas),
UltimoRGM AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM RGM),
UltimoRUES AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM RUES),
UltimoSaludTotal AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM SaludTotal),
UltimoSIMIT AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM SIMIT),
UltimoSOS AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM SOS),
UltimoTigo AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Tigo),
UltimoTigoUne AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM TigoUne),
UltimoCoosalud AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Coosalud),
UltimoCompensados AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta DESC) AS rn FROM Compensados)
SELECT
    TC.CarteraID,
    TC.LiderID,
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
    ISNULL(DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()), CP.FechaNacimiento) > GETDATE() THEN 1 ELSE 0 END, 0)  AS [Edad CajaCopi],
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
    ISNULL(TU.NumeroTelefono, 0)        AS [Celular TigoUne],      ISNULL(TU.Email, 0) AS [Email TigoUne],
    ISNULL(CS.NumeroTelefono, 0)        AS [Celular Coosalud],
    ISNULL(CS.Email, 0)                 AS [Email Coosalud],
    ISNULL(CS.Regimen, 0)               AS [Regimen Coosalud],
    ISNULL(CS.TipoAfiliado, 0)          AS [Tipo Afiliado Coosalud],
    ISNULL(CS.EstadoAfiliacion, 0)      AS [Estado Afiliacion Coosalud],
    ISNULL(CS.NivelSisben, 0)           AS [Nivel Sisben Coosalud],
    ISNULL(CS.DepartamentoOrigen, 0)    AS [Departamento Coosalud],
    ISNULL(CS.MunicipioOrigen, 0)       AS [Municipio Coosalud],
    ISNULL(CS.FechaNacimiento, '1900-01-01') AS [Fecha Nacimiento Coosalud],
    ISNULL(CAST(CS.EstadoEnCoosalud AS VARCHAR(1)), 0) AS [Estado Coosalud],
    ISNULL(CS.FechaConsulta, '1900-01-01') AS [Ultima Consulta Coosalud],
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
FROM Titular_Cartera TC
INNER JOIN Titular Ti                   ON Ti.NumeroDocumento     = TC.TitularID
LEFT JOIN UltimoEstadoTitular UST       ON UST.TitularID          = TC.TitularID AND UST.CarteraID = TC.CarteraID
LEFT JOIN DBRegistraduria.ani.Persona DBR ON Ti.NumeroDocumento   = DBR.NumeroDocumento
LEFT JOIN UltimoARL             A       ON TC.TitularID = A.TitularID              AND A.rn  = 1
LEFT JOIN UltimoCajacopi        CP      ON TC.TitularID = CP.TitularID             AND CP.rn = 1
LEFT JOIN UltimoCamara          C       ON TC.TitularID = C.TitularID              AND C.rn  = 1
LEFT JOIN UltimoDatosClaro      DC      ON TC.TitularID = DC.TitularID             AND DC.rn = 1
LEFT JOIN UltimoFosyga          F       ON TC.TitularID = F.TitularID              AND F.rn  = 1
LEFT JOIN UltimoMiVacuna        MV      ON TC.TitularID = MV.TitularID             AND MV.rn = 1
LEFT JOIN UltimoMutualSer       MS      ON TC.TitularID = MS.TitularID             AND MS.rn = 1
LEFT JOIN UltimoNuevaEps        NE      ON TC.TitularID = NE.TitularID             AND NE.rn = 1
LEFT JOIN UltimoPositiva        P       ON TC.TitularID = P.TitularID              AND P.rn  = 1
LEFT JOIN UltimoPositivaEmpresas PE     ON TC.TitularID = PE.TitularID             AND PE.rn = 1
LEFT JOIN UltimoRGM             RG      ON TC.TitularID = RG.TitularID             AND RG.rn = 1
LEFT JOIN UltimoRUES            RU      ON TC.TitularID = RU.TitularID             AND RU.rn = 1
LEFT JOIN UltimoSaludTotal      ST      ON TC.TitularID = ST.TitularID             AND ST.rn = 1
LEFT JOIN UltimoSIMIT           S       ON TC.TitularID = S.TitularID              AND S.rn  = 1
LEFT JOIN UltimoSOS             SO      ON TC.TitularID = SO.TitularID             AND SO.rn = 1
LEFT JOIN UltimoTigo            T       ON TC.TitularID = T.TitularID              AND T.rn  = 1
LEFT JOIN UltimoTigoUne         TU      ON TC.TitularID = TU.TitularID             AND TU.rn = 1
LEFT JOIN UltimoCoosalud        CS      ON TC.TitularID = CS.TitularID             AND CS.rn = 1
LEFT JOIN UltimoCompensados     CO      ON TC.TitularID = CO.TitularID             AND CO.rn = 1;




GO

-- 3) DROP/CREATE FUNCTION dbo.fn_BaseMadrePorCartera
IF OBJECT_ID('dbo.fn_BaseMadrePorCartera','IF') IS NOT NULL
    DROP FUNCTION dbo.fn_BaseMadrePorCartera;
GO
CREATE FUNCTION dbo.fn_BaseMadrePorCartera
(
    @CarteraID int,
    @LiderID   int = NULL
)
RETURNS TABLE
AS
RETURN
(
    WITH
    TitularesCartera AS (
        SELECT TitularID
        FROM dbo.Titular_Cartera
        WHERE CarteraID = @CarteraID
          AND (@LiderID IS NULL OR LiderID = @LiderID)
    ),
    UltimoARL              AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.ARLSura          WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoCajacopi         AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Cajacopi         WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoCamara           AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Camara           WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoDatosClaro       AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaInsercion DESC) AS rn FROM dbo.DatosClaro       WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoFosyga           AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Fosyga           WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoMiVacuna         AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.MiVacuna         WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoMutualSer        AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.MutualSer        WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoNuevaEps         AS (SELECT TRY_CAST(TitularID AS BIGINT) AS TitularID, NumeroTelefono, EstadoAfiliacion, TipoAfiliado, FechaNacimiento, Departamento, Municipio, EstadoEnNuevaEps, FechaConsulta, ROW_NUMBER() OVER (PARTITION BY TRY_CAST(TitularID AS BIGINT) ORDER BY FechaConsulta DESC) AS rn FROM dbo.NuevaEps WHERE TRY_CAST(TitularID AS BIGINT) IN (SELECT TitularID FROM TitularesCartera)),
    UltimoPositiva         AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Positiva         WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoPositivaEmpresas AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.PositivaEmpresas WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoRGM              AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.RGM              WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoRUES             AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.RUES             WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoSaludTotal       AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.SaludTotal       WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoSIMIT            AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.SIMIT            WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoSOS              AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.SOS              WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoTigo             AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Tigo             WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoTigoUne          AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.TigoUne          WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoCoosalud         AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Coosalud         WHERE TitularID IN (SELECT TitularID FROM TitularesCartera)),
    UltimoCompensados      AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY TitularID ORDER BY FechaConsulta  DESC) AS rn FROM dbo.Compensados      WHERE TitularID IN (SELECT TitularID FROM TitularesCartera))
    SELECT
        TC.CarteraID,
        TC.LiderID,
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
        ISNULL(DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, CP.FechaNacimiento, GETDATE()), CP.FechaNacimiento) > GETDATE() THEN 1 ELSE 0 END, 0)  AS [Edad CajaCopi],
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
        ISNULL(TU.NumeroTelefono, 0)        AS [Celular TigoUne],      ISNULL(TU.Email, 0) AS [Email TigoUne],
        ISNULL(CS.NumeroTelefono, 0)        AS [Celular Coosalud],
        ISNULL(CS.Email, 0)                 AS [Email Coosalud],
        ISNULL(CS.Regimen, 0)               AS [Regimen Coosalud],
        ISNULL(CS.TipoAfiliado, 0)          AS [Tipo Afiliado Coosalud],
        ISNULL(CS.EstadoAfiliacion, 0)      AS [Estado Afiliacion Coosalud],
        ISNULL(CS.NivelSisben, 0)           AS [Nivel Sisben Coosalud],
        ISNULL(CS.DepartamentoOrigen, 0)    AS [Departamento Coosalud],
        ISNULL(CS.MunicipioOrigen, 0)       AS [Municipio Coosalud],
        ISNULL(CS.FechaNacimiento, '1900-01-01') AS [Fecha Nacimiento Coosalud],
        ISNULL(CAST(CS.EstadoEnCoosalud AS VARCHAR(1)), 0) AS [Estado Coosalud],
        ISNULL(CS.FechaConsulta, '1900-01-01') AS [Ultima Consulta Coosalud],
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
    FROM dbo.Titular_Cartera TC
    INNER JOIN dbo.Titular Ti                  ON Ti.NumeroDocumento = TC.TitularID
    LEFT  JOIN dbo.UltimoEstadoTitular UST     ON UST.TitularID = TC.TitularID AND UST.CarteraID = TC.CarteraID
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
    LEFT  JOIN UltimoTigoUne          TU ON TC.TitularID = TU.TitularID AND TU.rn = 1
    LEFT  JOIN UltimoCoosalud         CS ON TC.TitularID = CS.TitularID AND CS.rn = 1
    LEFT  JOIN UltimoCompensados      CO ON TC.TitularID = CO.TitularID AND CO.rn = 1
    WHERE TC.CarteraID = @CarteraID
      AND (@LiderID IS NULL OR TC.LiderID = @LiderID)
);




GO

-- 4) DROP/CREATE FUNCTION dbo.fn_BaseMadreParaTitulares
IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares','IF') IS NOT NULL
    DROP FUNCTION dbo.fn_BaseMadreParaTitulares;
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
    UltimoTigoUne          AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.TigoUne          s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoCoosalud         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Coosalud         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
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
        ISNULL(TU.NumeroTelefono, 0)        AS [Celular TigoUne],      ISNULL(TU.Email, 0) AS [Email TigoUne],
        ISNULL(CS.NumeroTelefono, 0)        AS [Celular Coosalud],
        ISNULL(CS.Email, 0)                 AS [Email Coosalud],
        ISNULL(CS.Regimen, 0)               AS [Regimen Coosalud],
        ISNULL(CS.TipoAfiliado, 0)          AS [Tipo Afiliado Coosalud],
        ISNULL(CS.EstadoAfiliacion, 0)      AS [Estado Afiliacion Coosalud],
        ISNULL(CS.NivelSisben, 0)           AS [Nivel Sisben Coosalud],
        ISNULL(CS.DepartamentoOrigen, 0)    AS [Departamento Coosalud],
        ISNULL(CS.MunicipioOrigen, 0)       AS [Municipio Coosalud],
        ISNULL(CS.FechaNacimiento, '1900-01-01') AS [Fecha Nacimiento Coosalud],
        ISNULL(CAST(CS.EstadoEnCoosalud AS VARCHAR(1)), 0) AS [Estado Coosalud],
        ISNULL(CS.FechaConsulta, '1900-01-01') AS [Ultima Consulta Coosalud],
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
    LEFT  JOIN UltimoTigoUne          TU ON TC.TitularID = TU.TitularID AND TU.rn = 1
    LEFT  JOIN UltimoCoosalud         CS ON TC.TitularID = CS.TitularID AND CS.rn = 1
    LEFT  JOIN UltimoCompensados      CO ON TC.TitularID = CO.TitularID AND CO.rn = 1
);




GO

-- 5) DROP/CREATE FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica
IF OBJECT_ID('dbo.fn_BaseMadreParaTitulares_CarteraChica','IF') IS NOT NULL
    DROP FUNCTION dbo.fn_BaseMadreParaTitulares_CarteraChica;
GO

-- Copia exacta de dbo.fn_BaseMadreParaTitulares (ver
-- 2026-07-06_fn_BaseMadreParaTitulares.sql), salvo INNER HASH JOIN forzado
-- en las 6 CTEs que explotan en carteras chicas: Camara, DatosClaro,
-- Fosyga, MutualSer, Positiva, PositivaEmpresas. Las otras ~9 CTEs
-- (ARLSura, Cajacopi, MiVacuna, NuevaEps, RGM, RUES, SaludTotal, SIMIT,
-- SOS, Compensados) ya eleg€Ðan un plan eficiente por su cuenta Ÿ?" no se
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
    UltimoTigoUne          AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.TigoUne          s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
    UltimoCoosalud         AS (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.TitularID ORDER BY s.FechaConsulta  DESC) AS rn FROM dbo.Coosalud         s INNER JOIN @Titulares t ON t.TitularID = s.TitularID),
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
        ISNULL(TU.NumeroTelefono, 0)        AS [Celular TigoUne],      ISNULL(TU.Email, 0) AS [Email TigoUne],
        ISNULL(CS.NumeroTelefono, 0)        AS [Celular Coosalud],
        ISNULL(CS.Email, 0)                 AS [Email Coosalud],
        ISNULL(CS.Regimen, 0)               AS [Regimen Coosalud],
        ISNULL(CS.TipoAfiliado, 0)          AS [Tipo Afiliado Coosalud],
        ISNULL(CS.EstadoAfiliacion, 0)      AS [Estado Afiliacion Coosalud],
        ISNULL(CS.NivelSisben, 0)           AS [Nivel Sisben Coosalud],
        ISNULL(CS.DepartamentoOrigen, 0)    AS [Departamento Coosalud],
        ISNULL(CS.MunicipioOrigen, 0)       AS [Municipio Coosalud],
        ISNULL(CS.FechaNacimiento, '1900-01-01') AS [Fecha Nacimiento Coosalud],
        ISNULL(CAST(CS.EstadoEnCoosalud AS VARCHAR(1)), 0) AS [Estado Coosalud],
        ISNULL(CS.FechaConsulta, '1900-01-01') AS [Ultima Consulta Coosalud],
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
    LEFT  JOIN UltimoTigoUne          TU ON TC.TitularID = TU.TitularID AND TU.rn = 1
    LEFT  JOIN UltimoCoosalud         CS ON TC.TitularID = CS.TitularID AND CS.rn = 1
    LEFT  JOIN UltimoCompensados      CO ON TC.TitularID = CO.TitularID AND CO.rn = 1
);




GO

-- 6) Smoke (ejecutar manualmente tras aplicar):
-- SELECT TOP 5 [Celular Coosalud],[Email Coosalud],[Regimen Coosalud],[Departamento Coosalud],[Municipio Coosalud]
--   FROM dbo.fn_BaseMadrePorCartera(<cartera_chica>, NULL);
-- Nota: devolvera '0' mientras dbo.Coosalud siga vacia (esperado).

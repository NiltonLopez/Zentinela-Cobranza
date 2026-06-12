-- ============================================================================
-- Fuente NuevaEps -> vw_BaseMadre + fn_BaseMadrePorCartera
--   Fuente 1:1 (3557 filas / 3557 titulares). Contactabilidad (1 celular) +
--   localizacion (Departamento/Municipio) + afiliacion (Estado, TipoAfiliado,
--   FechaNacimiento). Se agregan 8 columnas a la vista y al iTVF.
--
--   OJO: NuevaEps.TitularID es varchar(30) (las demas fuentes son bigint). Se
--   usa TRY_CAST(TitularID AS BIGINT) en el CTE para que el JOIN con
--   Titular_Cartera (bigint) sea limpio y robusto ante valores no numericos.
--
--   Recordatorio critico: la vista se usa solo para el shape de EF; el codigo
--   productivo consume el iTVF. Ambos objetos se actualizan en esta migracion.
--   Idempotente. Fecha: 2026-06-12.
-- ============================================================================

-- 1) Indice de soporte (tabla heap)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NuevaEps_TitularID_FechaConsulta')
    CREATE INDEX IX_NuevaEps_TitularID_FechaConsulta
        ON dbo.NuevaEps (TitularID, FechaConsulta DESC);
GO

-- 2) ALTER VIEW dbo.vw_BaseMadre -- agrega CTE UltimoNuevaEps + 8 columnas + JOIN
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
LEFT JOIN UltimoCompensados     CO      ON TC.TitularID = CO.TitularID             AND CO.rn = 1;
GO

-- 3) DROP/CREATE FUNCTION dbo.fn_BaseMadrePorCartera -- mismas columnas, pre-restringido a la cartera
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
    LEFT  JOIN UltimoCompensados      CO ON TC.TitularID = CO.TitularID AND CO.rn = 1
    WHERE TC.CarteraID = @CarteraID
      AND (@LiderID IS NULL OR TC.LiderID = @LiderID)
);
GO

-- 4) Smoke (ejecutar manualmente tras aplicar):
-- SELECT TOP 5 [Celular NuevaEps],[Estado Afiliacion NuevaEps],[Departamento NuevaEps],[Municipio NuevaEps]
--   FROM dbo.fn_BaseMadrePorCartera(<cartera_chica>, NULL);

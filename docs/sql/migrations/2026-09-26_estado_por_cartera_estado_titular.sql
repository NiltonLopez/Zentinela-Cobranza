-- ============================================================================
-- Estado POR CARTERA del ETL de estado de titulares
--
--   La alerta general (vw_EtlEstadoTitularAlerta) solo sabe si la carga se publicó.
--   Si una cartera no baja de ControlNext, su CSV anterior se vuelve a cargar y la
--   carga se publica igual: nadie se entera. Esto lo hace visible.
--
--   EtlEstadoTitular_Archivo      una fila por CSV y por corrida (la llena cargar.ps1 en
--                                 modo publicar, con sp_EtlEstadoTitular_RegistrarArchivos)
--   vw_EtlEstadoTitularCarteras   la última corrida, una fila por cartera, con Estado:
--
--       NO BAJO          ControlNext no lo entregó en esta corrida: se cargó el CSV
--                        anterior, o nada si nunca hubo uno
--       ERROR LIMPIEZA   el CSV no se pudo leer o limpiar: la cartera no se cargó
--       VACIO            el CSV no trajo filas
--       ARCHIVO VIEJO    el CSV no está en la lista de descarga y tiene más de 1 día
--                        (nadie lo actualiza: se cargan datos viejos en cada corrida)
--       CAIDA DE FILAS   menos de la mitad de las filas de la corrida anterior
--       OK
--
--   Nada personal: solo nombre de archivo, fechas, conteos y el mensaje de error.
--   Se guardan 30 días.
-- ============================================================================
USE DBCallCenter;
GO

IF OBJECT_ID('dbo.EtlEstadoTitular_Archivo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EtlEstadoTitular_Archivo
    (
        ArchivoID       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EtlEstadoTitular_Archivo PRIMARY KEY,
        Corrida         DATETIME2(0)   NOT NULL,   -- igual para todos los CSV de una corrida
        Cartera         NVARCHAR(100)  NOT NULL,   -- nombre del CSV después de "de "
        FechaArchivo    DATETIME2(0)   NULL,       -- última modificación del CSV (= cuándo se descargó)
        EnDescarga      BIT            NOT NULL,   -- ¿está en la lista de descargar.py?
        Descargo        BIT            NULL,       -- NULL si no estaba en la descarga de esta corrida
        ErrorDescarga   NVARCHAR(400)  NULL,
        MB              DECIMAL(9,1)   NULL,
        SegundosDescarga DECIMAL(9,1)  NULL,
        Filas           INT            NULL,       -- filas tras la limpieza
        ErrorLimpieza   NVARCHAR(400)  NULL
    );
    CREATE NONCLUSTERED INDEX IX_EtlArchivo_Corrida ON dbo.EtlEstadoTitular_Archivo (Corrida DESC, Cartera);
END
GO

-- ----------------------------------------------------------------------------
-- Registrar una corrida. @Json: arreglo de objetos con las columnas de la tabla
-- (Cartera, FechaArchivo, EnDescarga, Descargo, ErrorDescarga, MB,
--  SegundosDescarga, Filas, ErrorLimpieza). FechaArchivo en ISO 8601.
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_EtlEstadoTitular_RegistrarArchivos
    @Json NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @corrida DATETIME2(0) = SYSDATETIME();

    INSERT INTO dbo.EtlEstadoTitular_Archivo
        (Corrida, Cartera, FechaArchivo, EnDescarga, Descargo, ErrorDescarga, MB,
         SegundosDescarga, Filas, ErrorLimpieza)
    SELECT @corrida, j.Cartera, TRY_CONVERT(DATETIME2(0), j.FechaArchivo, 126),
           ISNULL(j.EnDescarga, 0), j.Descargo, LEFT(j.ErrorDescarga, 400), j.MB,
           j.SegundosDescarga, j.Filas, LEFT(j.ErrorLimpieza, 400)
    FROM OPENJSON(@Json) WITH (
        Cartera          NVARCHAR(100)  '$.Cartera',
        FechaArchivo     NVARCHAR(40)   '$.FechaArchivo',
        EnDescarga       BIT            '$.EnDescarga',
        Descargo         BIT            '$.Descargo',
        ErrorDescarga    NVARCHAR(4000) '$.ErrorDescarga',
        MB               DECIMAL(9,1)   '$.MB',
        SegundosDescarga DECIMAL(9,1)   '$.SegundosDescarga',
        Filas            INT            '$.Filas',
        ErrorLimpieza    NVARCHAR(4000) '$.ErrorLimpieza'
    ) j
    WHERE j.Cartera IS NOT NULL;

    DELETE FROM dbo.EtlEstadoTitular_Archivo WHERE Corrida < DATEADD(DAY, -30, @corrida);

    SELECT @@ROWCOUNT AS Purgadas, @corrida AS Corrida;
END
GO

CREATE OR ALTER VIEW dbo.vw_EtlEstadoTitularCarteras
AS
WITH Ultima AS (
    SELECT MAX(Corrida) AS Corrida FROM dbo.EtlEstadoTitular_Archivo
),
Actual AS (
    SELECT a.*
    FROM dbo.EtlEstadoTitular_Archivo a
    JOIN Ultima u ON u.Corrida = a.Corrida
)
SELECT
    a.Corrida,
    a.Cartera,
    CASE
        WHEN a.EnDescarga = 1 AND ISNULL(a.Descargo, 0) = 0                   THEN 'NO BAJO'
        WHEN a.ErrorLimpieza IS NOT NULL                                      THEN 'ERROR LIMPIEZA'
        WHEN ISNULL(a.Filas, 0) = 0                                           THEN 'VACIO'
        WHEN a.EnDescarga = 0 AND a.FechaArchivo < DATEADD(DAY, -1, a.Corrida) THEN 'ARCHIVO VIEJO'
        WHEN ant.Filas > 0 AND a.Filas < ant.Filas / 2                        THEN 'CAIDA DE FILAS'
        ELSE 'OK'
    END AS Estado,
    COALESCE(a.ErrorDescarga, a.ErrorLimpieza) AS Detalle,
    a.FechaArchivo,
    DATEDIFF(HOUR, a.FechaArchivo, a.Corrida) AS HorasArchivo,
    a.Filas,
    ant.Filas AS FilasCorridaAnterior,
    ok.UltimaDescargaOk,
    a.MB,
    a.SegundosDescarga
FROM Actual a
OUTER APPLY (
    SELECT TOP (1) p.Filas
    FROM dbo.EtlEstadoTitular_Archivo p
    -- La referencia es la última corrida en que la cartera cargó BIEN: contra una
    -- corrida rota (0 filas) no se puede ver una caída.
    WHERE p.Cartera = a.Cartera AND p.Corrida < a.Corrida AND p.Filas > 0 AND p.ErrorLimpieza IS NULL
    ORDER BY p.Corrida DESC
) ant
OUTER APPLY (
    SELECT MAX(p.Corrida) AS UltimaDescargaOk
    FROM dbo.EtlEstadoTitular_Archivo p
    WHERE p.Cartera = a.Cartera AND p.Descargo = 1
) ok;
GO

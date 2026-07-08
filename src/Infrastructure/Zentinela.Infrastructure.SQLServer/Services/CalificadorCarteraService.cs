using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Calcula las métricas del Calificador de Cartera. Toda la agregación vive en SQL:
/// un único batch materializa la cartera filtrada en una temporal y emite 9 result
/// sets (escalares + 7 desgloses + localización). C# sólo hidrata el DTO. Reemplaza
/// los 9 bloques C# en memoria que iteraban sobre 60-190k filas y, desde esta
/// optimización, también la segunda pasada del TVF que calculaba localización.
///
/// La lista de titulares de la cartera se calcula UNA sola vez (query previa,
/// ~0.7s) y se pasa como TVP (dbo.TitularIDList) a dbo.fn_BaseMadreParaTitulares,
/// que cada fuente cruza directamente por JOIN. Reemplaza a fn_BaseMadrePorCartera
/// (usada por EscribirLocalizacionCsvAsync y por el Creador, sin cambios), cuya CTE
/// TitularesCartera se reevaluaba 18 veces por llamada (confirmado con el plan de
/// ejecución real) — dominaba ~16.8 de ~25s en una cartera de 147k filas.
/// </summary>
public class CalificadorCarteraService : ICalificadorCarteraService
{
    private readonly CallCenterDbContext _db;
    private readonly ICalificadorCarteraCache _cache;

    public CalificadorCarteraService(CallCenterDbContext db, ICalificadorCarteraCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.LiderID.HasValue
            && contexto.Rol is Roles.Lider or Roles.LiderExterno)
        {
            // Líder (interno o externo): solo sus combinaciones expuestas en vw_BaseMadre.
            // No incluye "Cartera Completo" — un líder no puede ver datos de otros líderes.
            var lid = contexto.LiderID.Value;
            return await _db.BaseMadreRows
                // Excluye combinaciones de relaciones inactivadas (ADR-022): aunque la
                // vista conserve filas históricas, una relación retirada no se ofrece.
                .Where(b => b.LiderID == lid
                         && _db.LiderCarteras.Any(lc =>
                                lc.LiderID == b.LiderID && lc.CarteraID == b.CarteraID && lc.Activo))
                .Select(b => new { b.CarteraID, b.LiderID })
                .Distinct()
                .Join(_db.Carteras,
                    b => b.CarteraID,
                    c => c.ID,
                    (b, c) => new { b.CarteraID, b.LiderID, c.NombreCartera })
                .Join(_db.Lideres,
                    x => x.LiderID,
                    l => l.ID,
                    (x, l) => new CarteraLiderDropdownDto
                    {
                        CarteraID = x.CarteraID,
                        LiderID   = x.LiderID,
                        Etiqueta  = x.NombreCartera + " — " + l.Nombre,
                    })
                .OrderBy(o => o.Etiqueta)
                .AsNoTracking()
                .ToListAsync();
        }

        // Director / Admin: combinaciones individuales líder-cartera +
        // una entrada agregada "{NombreCartera} Completo" por cartera (LiderID = null).
        // Mismo patrón que CreadorZentinelaService. Solo relaciones activas (ADR-022).
        var combinaciones = await _db.LiderCarteras
            .Where(lc => lc.Activo)
            .Select(lc => new
            {
                lc.CarteraID,
                lc.LiderID,
                NombreCartera = lc.Cartera.NombreCartera,
                NombreLider   = lc.Lider.Nombre,
            })
            .AsNoTracking()
            .ToListAsync();

        var resultado = new List<CarteraLiderDropdownDto>();
        foreach (var grupo in combinaciones.GroupBy(c => new { c.CarteraID, c.NombreCartera }))
        {
            foreach (var c in grupo)
            {
                resultado.Add(new CarteraLiderDropdownDto
                {
                    CarteraID = c.CarteraID,
                    LiderID   = c.LiderID,
                    Etiqueta  = c.NombreCartera + " — " + c.NombreLider,
                });
            }

            // Entrada agregada de la cartera completa (sin discriminación por líder)
            resultado.Add(new CarteraLiderDropdownDto
            {
                CarteraID = grupo.Key.CarteraID,
                LiderID   = null,
                Etiqueta  = grupo.Key.NombreCartera + " Completo",
            });
        }

        return resultado.OrderBy(o => o.Etiqueta).ToList();
    }

    // ── Métricas ──────────────────────────────────────────────────────────────

    /// <summary>
    /// SQL batch que materializa la cartera en #t (con columnas pre-parseadas y todas
    /// las columnas necesarias para localización) y emite 9 result sets. Reemplaza los
    /// 9 bloques C# que iteraban filas en memoria + la segunda pasada del TVF que
    /// calculaba localización. Los nombres de columna y orden de filas replican
    /// exactamente lo que producía el código C# (Cantidad DESC, OrdenRango para
    /// Rotación, top 10 + Otras para EPS).
    ///
    /// FROM dbo.fn_BaseMadreParaTitulares(@CarteraID, @Titulares): recibe la lista de
    /// titulares ya calculada (ver CalificarAsync) en vez de recalcularla — @LiderID
    /// no se pasa aquí porque el filtro de líder ya quedó aplicado en esa lista.
    /// </summary>
    private const string SqlAgregados = """
        SET NOCOUNT ON;

        SELECT
            [Saldo],
            [Ultima Fecha Gestion]            AS UltimaFechaGestion,
            [Fecha Promesa]                   AS FechaPromesa,
            [Valor Promesa]                   AS ValorPromesa,
            [Rotacion],
            [Ultimo Perfil]                   AS UltimoPerfil,
            [EPS], [Regimen],
            [Tipo Afiliado]                   AS TipoAfiliado,
            [Departamento],
            [Celular Cajacopi]                AS CelCajacopi,
            [Celular Camara]                  AS CelCamara,
            [Celular Salud Total]             AS CelSaludTotal,
            [Celular Positiva]                AS CelPositiva,
            [Celular Tigo]                    AS CelTigo,
            [Celular SOS]                     AS CelSOS,
            [Celular Claro]                   AS CelClaro,
            [Celular MutualSer]               AS CelMutualSer,
            [Celular 2 MutualSer]             AS CelMutualSer2,
            [Celular MiVacuna]                AS CelMiVacuna,
            [Celular NuevaEps]                AS CelNuevaEps,
            [Email CajaCopi]                  AS EmCajacopi,
            [Email Camara]                    AS EmCamara,
            [Email Salud Total]               AS EmSaludTotal,
            [Email Positiva]                  AS EmPositiva,
            [Email Tigo]                      AS EmTigo,
            [Email Claro]                     AS EmClaro,
            [Email MutualSer]                 AS EmMutualSer,
            [Email MiVacuna]                  AS EmMiVacuna,
            [Edad Registraduria]              AS EdadReg,
            TRY_CAST([Saldo]                       AS decimal(18,2))  AS _Saldo,
            TRY_CAST(NULLIF([Valor Promesa], '0')  AS decimal(18,2))  AS _ValorPromesa,
            TRY_CAST([Rotacion]                    AS int)            AS _Rotacion,
            TRY_CONVERT(date, [Fecha Promesa], 103)                   AS _FechaPromesa,
            -- Columnas extra para localización (result set 9)
            TRY_CAST([Numero Documento] AS bigint) AS TitularID,
            [Edad CajaCopi]               AS EdadCajacopi,
            [NIT Salud Total]             AS NITSaludTotal,
            [Empresa Salud Total]         AS EmpresaSaludTotal,
            [EPS en Compensados]          AS EPSCompensados,
            [Tipo Afiliado Compensados]   AS TipoAfiliadoCompensados,
            [Edad Positiva]               AS EdadPositiva,
            [Nit Sos]                     AS NitSOS,
            [Empresa SOS]                 AS EmpresaSOS,
            [Departamento MutualSer]      AS DepartamentoMutualSer,
            [Municipio MutualSer]         AS MunicipioMutualSer,
            [Departamento NuevaEps]       AS DepartamentoNuevaEps,
            [Municipio NuevaEps]          AS MunicipioNuevaEps,
            [NIT Sura]                    AS NITSura,
            [Empresa Sura]                AS EmpresaSura,
            [NIT Positiva]                AS NITPositiva,
            [Empresa Positiva]            AS EmpresaPositiva,
            [Empresa RUES]                AS EmpresaRUES,
            [PLACA SIMIT]                 AS PlacaSIMIT,
            Marca,
            Modelo,
            Placa
        INTO #t
        FROM dbo.fn_BaseMadreParaTitulares(@CarteraID, @Titulares)
        -- OPTION (RECOMPILE) es OBLIGATORIO, no un ajuste opcional: sin él, el
        -- optimizador asume que @Titulares (TVP) trae ~1 fila sin importar cuántas
        -- traiga en realidad, y arma un plan de nested loops que para una cartera
        -- de 147k filas NUNCA termina (medido: sesión matada tras 6+ min sin
        -- avanzar). Con RECOMPILE, el optimizador mide el tamaño real de @Titulares
        -- en cada ejecución y arma un plan correcto (medido: 3.3s).
        -- MAXDOP 4 (servidor de 8 núcleos, 2026-07-08): sin tope, cada llamada pide
        -- los 8 núcleos completos; dos usuarios calificando a la vez se ahogan entre
        -- sí (incidente: retry storm en carteras chicas, ver
        -- project_calificador_retry_storm.md). Con el tope, cada corrida es algo
        -- más lenta sola pero el servidor resiste concurrencia real.
        OPTION (RECOMPILE, MAXDOP 4);

        DECLARE @Total int  = (SELECT COUNT(*) FROM #t);
        DECLARE @Hoy   date = CAST(GETDATE() AS date);

        -- Result set 1: escalares
        SELECT
            @Total                                                                  AS Total,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM #t WHERE [Saldo] NOT IN ('','0'))
                      THEN 1 ELSE 0 END AS bit)                                     AS GestionDisponible,
            ISNULL((SELECT SUM(_Saldo) FROM #t), 0)                                 AS SaldoTotal,
            ISNULL((SELECT AVG(_Saldo) FROM #t), 0)                                 AS SaldoPromedio,
            (SELECT COUNT(*) FROM #t WHERE UltimaFechaGestion NOT IN ('','0'))      AS TitularesGestionados,
            (SELECT COUNT(*) FROM #t WHERE _FechaPromesa >= @Hoy)                   AS TitularesConAcuerdoVigente,
            ISNULL((SELECT SUM(_ValorPromesa) FROM #t), 0)                          AS ValorTotalPromesas,
            CASE WHEN @Total > 0
                 THEN (SELECT CAST(ROUND(AVG(CAST(ISNULL(_Rotacion, 0) AS decimal(18,4))), 1)
                                   AS decimal(10,1)) FROM #t)
                 ELSE NULL END                                                      AS RotacionPromedio,
            (SELECT COUNT(*) FROM #t
                WHERE EPS NOT IN ('','0') OR Regimen NOT IN ('','0')
                   OR TipoAfiliado NOT IN ('','0'))                                 AS TitularesConFosyga,
            (SELECT COUNT(*) FROM #t
                WHERE CelCajacopi   NOT IN ('','0') OR CelCamara    NOT IN ('','0')
                   OR CelSaludTotal NOT IN ('','0') OR CelPositiva  NOT IN ('','0')
                   OR CelTigo       NOT IN ('','0') OR CelSOS       NOT IN ('','0')
                   OR CelClaro      NOT IN ('','0') OR CelMutualSer NOT IN ('','0')
                   OR CelMutualSer2 NOT IN ('','0') OR CelMiVacuna  NOT IN ('','0')
                   OR CelNuevaEps   NOT IN ('','0')) AS ConCelular,
            (SELECT COUNT(*) FROM #t
                WHERE EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                   OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                   OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                   OR EmMutualSer  NOT IN ('','0') OR EmMiVacuna NOT IN ('','0'))   AS ConEmail,
            (SELECT COUNT(*) FROM #t
                WHERE (CelCajacopi   NOT IN ('','0') OR CelCamara    NOT IN ('','0')
                    OR CelSaludTotal NOT IN ('','0') OR CelPositiva  NOT IN ('','0')
                    OR CelTigo       NOT IN ('','0') OR CelSOS       NOT IN ('','0')
                    OR CelClaro      NOT IN ('','0') OR CelMutualSer NOT IN ('','0')
                    OR CelMutualSer2 NOT IN ('','0') OR CelMiVacuna NOT IN ('','0')
                    OR CelNuevaEps   NOT IN ('','0'))
                  AND (EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                    OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                    OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                    OR EmMutualSer  NOT IN ('','0') OR EmMiVacuna NOT IN ('','0'))) AS ConCelularYEmail,
            (SELECT COUNT(*) FROM #t
                WHERE NOT (CelCajacopi   NOT IN ('','0') OR CelCamara    NOT IN ('','0')
                        OR CelSaludTotal NOT IN ('','0') OR CelPositiva  NOT IN ('','0')
                        OR CelTigo       NOT IN ('','0') OR CelSOS       NOT IN ('','0')
                        OR CelClaro      NOT IN ('','0') OR CelMutualSer NOT IN ('','0')
                        OR CelMutualSer2 NOT IN ('','0') OR CelMiVacuna NOT IN ('','0')
                        OR CelNuevaEps   NOT IN ('','0'))
                  AND NOT (EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                        OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                        OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                        OR EmMutualSer  NOT IN ('','0') OR EmMiVacuna NOT IN ('','0'))) AS SinContacto,
            (SELECT COUNT(*) FROM #t WHERE EdadReg > 0)                             AS ConEdadRegistraduria,
            CASE WHEN (SELECT COUNT(*) FROM #t WHERE EdadReg > 0) > 0
                 THEN (SELECT CAST(ROUND(AVG(CAST(EdadReg AS decimal(18,4))), 1) AS decimal(10,1))
                         FROM #t WHERE EdadReg > 0)
                 ELSE NULL END                                                      AS EdadPromedioRegistraduria;

        -- Result set 2: desgloseRotacion (orden por OrdenRango)
        WITH R AS (
            SELECT CASE
                      WHEN ISNULL(_Rotacion, 0) = 0 THEN '0'
                      WHEN ISNULL(_Rotacion, 0) <= 5 THEN '1-5'
                      WHEN ISNULL(_Rotacion, 0) <= 10 THEN '6-10'
                      WHEN ISNULL(_Rotacion, 0) <= 20 THEN '11-20'
                      ELSE '+20'
                   END AS Etiqueta
            FROM #t
        )
        SELECT
            Etiqueta,
            COUNT(*)                                                                AS Cantidad,
            CAST(ROUND(CAST(COUNT(*) AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1)
                 AS decimal(10,1))                                                  AS Porcentaje
        FROM R
        GROUP BY Etiqueta
        ORDER BY CASE Etiqueta WHEN '0' THEN 0 WHEN '1-5' THEN 1 WHEN '6-10' THEN 2
                               WHEN '11-20' THEN 3 WHEN '+20' THEN 4 ELSE 99 END;

        -- Result set 3: desglosePerfil
        SELECT UltimoPerfil AS Etiqueta, COUNT(*) AS Cantidad,
               CAST(ROUND(CAST(COUNT(*) AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM #t WHERE UltimoPerfil NOT IN ('','0')
        GROUP BY UltimoPerfil ORDER BY COUNT(*) DESC;

        -- Result set 4: desgloseTipoAfiliado
        SELECT TipoAfiliado AS Etiqueta, COUNT(*) AS Cantidad,
               CAST(ROUND(CAST(COUNT(*) AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM #t WHERE TipoAfiliado NOT IN ('','0')
        GROUP BY TipoAfiliado ORDER BY COUNT(*) DESC;

        -- Result set 5: desgloseRegimen
        SELECT Regimen AS Etiqueta, COUNT(*) AS Cantidad,
               CAST(ROUND(CAST(COUNT(*) AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM #t WHERE Regimen NOT IN ('','0')
        GROUP BY Regimen ORDER BY COUNT(*) DESC;

        -- Result set 6: desgloseEPS (top 10 + Otras)
        ;WITH EPS_Counts AS (
            SELECT EPS AS Etiqueta, COUNT(*) AS Cantidad,
                   ROW_NUMBER() OVER (ORDER BY COUNT(*) DESC, EPS) AS rn
            FROM #t WHERE EPS NOT IN ('','0') GROUP BY EPS
        ),
        Top10 AS (SELECT Etiqueta, Cantidad, rn FROM EPS_Counts WHERE rn <= 10),
        Otras AS (
            SELECT '(Otras)' AS Etiqueta, SUM(Cantidad) AS Cantidad, 11 AS rn
            FROM EPS_Counts WHERE rn > 10 HAVING SUM(Cantidad) > 0
        )
        SELECT Etiqueta, Cantidad,
               CAST(ROUND(CAST(Cantidad AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM (SELECT * FROM Top10 UNION ALL SELECT * FROM Otras) u
        ORDER BY rn;

        -- Result set 7: desgloseDepartamento
        SELECT Departamento AS Etiqueta, COUNT(*) AS Cantidad,
               CAST(ROUND(CAST(COUNT(*) AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM #t WHERE Departamento NOT IN ('','0')
        GROUP BY Departamento ORDER BY COUNT(*) DESC;

        -- Result set 8: desgloseFuentesCelular
        ;WITH F AS (
            SELECT 'Cajacopi' AS Fuente, SUM(CASE WHEN CelCajacopi   NOT IN ('','0') THEN 1 ELSE 0 END) AS Cantidad FROM #t
            UNION ALL SELECT 'Camara',     SUM(CASE WHEN CelCamara     NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'SaludTotal', SUM(CASE WHEN CelSaludTotal NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'Positiva',   SUM(CASE WHEN CelPositiva   NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'Tigo',       SUM(CASE WHEN CelTigo       NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'SOS',        SUM(CASE WHEN CelSOS        NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'Claro',      SUM(CASE WHEN CelClaro      NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'MutualSer (1)', SUM(CASE WHEN CelMutualSer  NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'MutualSer (2)', SUM(CASE WHEN CelMutualSer2 NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'MiVacuna',      SUM(CASE WHEN CelMiVacuna   NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
            UNION ALL SELECT 'NuevaEps',      SUM(CASE WHEN CelNuevaEps   NOT IN ('','0') THEN 1 ELSE 0 END) FROM #t
        )
        SELECT Fuente AS Etiqueta, Cantidad,
               CAST(ROUND(CAST(Cantidad AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM F WHERE Cantidad > 0 ORDER BY Cantidad DESC;

        -- Result set 9: localización (15 fuentes, encontrados/no-encontrados)
        -- Reusa #t y agrega LEFT JOIN DISTINCT contra cada tabla fuente para
        -- distinguir "no encontrado" (existe fila pero datos vacíos) de
        -- "falta buscar" (no existe fila en la tabla fuente).
        SELECT
            SUM(CASE WHEN NULLIF(NULLIF(t.CelCajacopi,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmCajacopi,  ''), '0') IS NOT NULL
                       OR (t.EdadCajacopi IS NOT NULL AND t.EdadCajacopi != 0)
                     THEN 1 ELSE 0 END) AS CajacopiEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelCajacopi,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmCajacopi,  ''), '0') IS NULL
                      AND (t.EdadCajacopi IS NULL OR t.EdadCajacopi = 0)
                      AND cj.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS CajacopiNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelCamara,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmCamara,  ''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS CamaraEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelCamara,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmCamara,  ''), '0') IS NULL
                      AND cm.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS CamaraNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelSaludTotal,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmSaludTotal,  ''), '0') IS NOT NULL
                       OR (t.NITSaludTotal IS NOT NULL AND t.NITSaludTotal != 0)
                       OR NULLIF(NULLIF(t.EmpresaSaludTotal,''),'0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS SaludTotalEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelSaludTotal,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmSaludTotal,  ''), '0') IS NULL
                      AND (t.NITSaludTotal IS NULL OR t.NITSaludTotal = 0)
                      AND NULLIF(NULLIF(t.EmpresaSaludTotal,''),'0') IS NULL
                      AND st.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS SaludTotalNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EPS,         ''), '0') IS NOT NULL
                       OR NULLIF(NULLIF(t.Regimen,     ''), '0') IS NOT NULL
                       OR NULLIF(NULLIF(t.TipoAfiliado,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS FosygaEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EPS,         ''), '0') IS NULL
                      AND NULLIF(NULLIF(t.Regimen,     ''), '0') IS NULL
                      AND NULLIF(NULLIF(t.TipoAfiliado,''), '0') IS NULL
                      AND f.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS FosygaNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EPSCompensados,         ''), '0') IS NOT NULL
                       OR NULLIF(NULLIF(t.TipoAfiliadoCompensados,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS CompensadosEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EPSCompensados,         ''), '0') IS NULL
                      AND NULLIF(NULLIF(t.TipoAfiliadoCompensados,''), '0') IS NULL
                      AND co.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS CompensadosNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelPositiva,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmPositiva,  ''), '0') IS NOT NULL
                       OR (t.EdadPositiva IS NOT NULL AND t.EdadPositiva != 0)
                     THEN 1 ELSE 0 END) AS PositivaEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelPositiva,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmPositiva,  ''), '0') IS NULL
                      AND (t.EdadPositiva IS NULL OR t.EdadPositiva = 0)
                      AND p.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS PositivaNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelTigo,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmTigo,  ''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS TigoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelTigo,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmTigo,  ''), '0') IS NULL
                      AND ti.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS TigoNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelSOS,    ''), '0') IS NOT NULL
                       OR (t.NitSOS IS NOT NULL AND t.NitSOS != 0)
                       OR NULLIF(NULLIF(t.EmpresaSOS,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS SosEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelSOS,    ''), '0') IS NULL
                      AND (t.NitSOS IS NULL OR t.NitSOS = 0)
                      AND NULLIF(NULLIF(t.EmpresaSOS,''), '0') IS NULL
                      AND s.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS SosNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelClaro,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmClaro,  ''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS ClaroEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelClaro,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmClaro,  ''), '0') IS NULL
                      AND dc.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS ClaroNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelMiVacuna,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmMiVacuna,  ''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS MiVacunaEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelMiVacuna,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmMiVacuna,  ''), '0') IS NULL
                      AND mv.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS MiVacunaNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelMutualSer, ''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.CelMutualSer2,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.EmMutualSer,  ''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.DepartamentoMutualSer,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.MunicipioMutualSer,   ''),'0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS MutualSerEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelMutualSer, ''),'0') IS NULL
                      AND NULLIF(NULLIF(t.CelMutualSer2,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.EmMutualSer,  ''),'0') IS NULL
                      AND NULLIF(NULLIF(t.DepartamentoMutualSer,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.MunicipioMutualSer,   ''),'0') IS NULL
                      AND ms.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS MutualSerNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelNuevaEps,         ''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.DepartamentoNuevaEps,''),'0') IS NOT NULL
                       OR NULLIF(NULLIF(t.MunicipioNuevaEps,   ''),'0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS NuevaEpsEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.CelNuevaEps,         ''),'0') IS NULL
                      AND NULLIF(NULLIF(t.DepartamentoNuevaEps,''),'0') IS NULL
                      AND NULLIF(NULLIF(t.MunicipioNuevaEps,   ''),'0') IS NULL
                      AND ne.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS NuevaEpsNoEnc,
            SUM(CASE WHEN (t.NITSura IS NOT NULL AND t.NITSura != 0)
                       OR NULLIF(NULLIF(t.EmpresaSura,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS SuraEnc,
            SUM(CASE WHEN (t.NITSura IS NULL OR t.NITSura = 0)
                      AND NULLIF(NULLIF(t.EmpresaSura,''), '0') IS NULL
                      AND ar.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS SuraNoEnc,
            SUM(CASE WHEN (t.NITPositiva IS NOT NULL AND t.NITPositiva != 0)
                       OR NULLIF(NULLIF(t.EmpresaPositiva,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS PosEmpEnc,
            SUM(CASE WHEN (t.NITPositiva IS NULL OR t.NITPositiva = 0)
                      AND NULLIF(NULLIF(t.EmpresaPositiva,''), '0') IS NULL
                      AND pe.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS PosEmpNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EmpresaRUES,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS RuesEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.EmpresaRUES,''), '0') IS NULL
                      AND ru.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS RuesNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.PlacaSIMIT,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS SimitEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.PlacaSIMIT,''), '0') IS NULL
                      AND si.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS SimitNoEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.Marca,''), '0') IS NOT NULL
                       OR (t.Modelo IS NOT NULL AND t.Modelo != 0)
                       OR NULLIF(NULLIF(t.Placa,''), '0') IS NOT NULL
                     THEN 1 ELSE 0 END) AS RgmEnc,
            SUM(CASE WHEN NULLIF(NULLIF(t.Marca,''), '0') IS NULL
                      AND (t.Modelo IS NULL OR t.Modelo = 0)
                      AND NULLIF(NULLIF(t.Placa,''), '0') IS NULL
                      AND rg.TitularID IS NOT NULL
                     THEN 1 ELSE 0 END) AS RgmNoEnc
        FROM #t t
        LEFT JOIN (SELECT DISTINCT TitularID FROM Cajacopi)         cj ON cj.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM Camara)           cm ON cm.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM SaludTotal)       st ON st.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM Fosyga)           f  ON f.TitularID  = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM Compensados)      co ON co.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM Positiva)         p  ON p.TitularID  = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM Tigo)             ti ON ti.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM SOS)              s  ON s.TitularID  = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM DatosClaro)       dc ON dc.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM MiVacuna)         mv ON mv.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM MutualSer)        ms ON ms.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TRY_CAST(TitularID AS BIGINT) AS TitularID FROM NuevaEps) ne ON ne.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM ARLSura)          ar ON ar.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM PositivaEmpresas) pe ON pe.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM RUES)             ru ON ru.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM SIMIT)            si ON si.TitularID = t.TitularID
        LEFT JOIN (SELECT DISTINCT TitularID FROM RGM)              rg ON rg.TitularID = t.TitularID;

        DROP TABLE #t;
        """;

    // Carteras pesadas (P99 ~190k filas, admin sin líder ~395k) + cold cache pueden
    // sumar 30-60s entre TVF, materialización y desgloses. El default de 30s para
    // ADO.NET y EF Core no alcanza — subimos a 3 min para dar margen.
    private const int CommandTimeoutSegundos = 180;

    public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int? liderID, CancellationToken ct = default)
    {
        // Hit de caché: el batch de 9 result sets cuesta 1-3s P90 y las recargas
        // son muy frecuentes. Si hay entry vigente la devolvemos directo (<100ms).
        if (_cache.TryGet(carteraID, liderID, out var cached))
            return cached!;

        _db.Database.SetCommandTimeout(CommandTimeoutSegundos);

        var conn = _db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            var dto = new CalificadorCarteraDto();

            // Precalcula la lista de TitularID UNA sola vez (antes se recalculaba 18
            // veces dentro del TVF vía la CTE TitularesCartera — dominaba ~16.8 de
            // ~25s en una cartera de 147k filas). Ver
            // docs/sql/migrations/2026-07-06_fn_BaseMadreParaTitulares.sql.
            var titulares = new DataTable();
            titulares.Columns.Add("TitularID", typeof(long));

            await using (var cmdIds = conn.CreateCommand())
            {
                cmdIds.CommandText = """
                    SELECT TitularID
                    FROM dbo.Titular_Cartera
                    WHERE CarteraID = @CarteraID AND (@LiderID IS NULL OR LiderID = @LiderID)
                    """;
                cmdIds.CommandTimeout = CommandTimeoutSegundos;
                AddParam(cmdIds, "@CarteraID", carteraID);
                AddParam(cmdIds, "@LiderID",   (object?)liderID ?? DBNull.Value);

                await using var readerIds = await cmdIds.ExecuteReaderAsync(ct);
                while (await readerIds.ReadAsync(ct))
                    titulares.Rows.Add(readerIds.GetInt64(0));
            }

            // Cartera sin titulares para este filtro: mismo comportamiento que ya
            // existía para "sin datos", sin necesidad de invocar el TVF con un TVP vacío.
            if (titulares.Rows.Count == 0)
                return new CalificadorCarteraDto { TotalTitulares = 0 };

            await using var cmd = (SqlCommand)conn.CreateCommand();
            cmd.CommandText    = SqlAgregados;
            cmd.CommandTimeout = CommandTimeoutSegundos;
            AddParam(cmd, "@CarteraID", carteraID);
            cmd.Parameters.Add(new SqlParameter("@Titulares", SqlDbType.Structured)
            {
                TypeName = "dbo.TitularIDList",
                Value    = titulares,
            });

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Result set 1 — escalares
            if (!await reader.ReadAsync(ct))
                return new CalificadorCarteraDto { TotalTitulares = 0 };

            var total = reader.GetInt32(reader.GetOrdinal("Total"));
            if (total == 0)
            {
                await reader.CloseAsync();
                return new CalificadorCarteraDto { TotalTitulares = 0 };
            }

            dto.TotalTitulares             = total;
            dto.GestionDisponible          = reader.GetBoolean(reader.GetOrdinal("GestionDisponible"));
            dto.SaldoTotal                 = reader.GetDecimal(reader.GetOrdinal("SaldoTotal"));
            dto.SaldoPromedio              = reader.GetDecimal(reader.GetOrdinal("SaldoPromedio"));
            dto.TitularesGestionados       = reader.GetInt32(reader.GetOrdinal("TitularesGestionados"));
            dto.TitularesConAcuerdoVigente = reader.GetInt32(reader.GetOrdinal("TitularesConAcuerdoVigente"));
            dto.ValorTotalPromesas         = reader.GetDecimal(reader.GetOrdinal("ValorTotalPromesas"));
            dto.RotacionPromedio           = await GetNullableDecimalAsync(reader, "RotacionPromedio");
            dto.TitularesConFosyga         = reader.GetInt32(reader.GetOrdinal("TitularesConFosyga"));
            dto.ConCelular                 = reader.GetInt32(reader.GetOrdinal("ConCelular"));
            dto.ConEmail                   = reader.GetInt32(reader.GetOrdinal("ConEmail"));
            dto.ConCelularYEmail           = reader.GetInt32(reader.GetOrdinal("ConCelularYEmail"));
            dto.SinContacto                = reader.GetInt32(reader.GetOrdinal("SinContacto"));
            dto.ConEdadRegistraduria       = reader.GetInt32(reader.GetOrdinal("ConEdadRegistraduria"));
            dto.SinEdadRegistraduria       = total - dto.ConEdadRegistraduria;
            dto.EdadPromedioRegistraduria  = await GetNullableDecimalAsync(reader, "EdadPromedioRegistraduria");

            // Result sets 2-8 — desgloses (etiqueta, cantidad, porcentaje)
            await reader.NextResultAsync(ct); dto.DesgloseRotacion       = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesglosePerfil         = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesgloseTipoAfiliado   = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesgloseRegimen        = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesgloseEPS            = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesgloseDepartamento   = await ReadDesgloseAsync(reader, ct);
            await reader.NextResultAsync(ct); dto.DesgloseFuentesCelular = await ReadDesgloseAsync(reader, ct);

            // Result set 9 — localización (15 fuentes, una sola fila)
            await reader.NextResultAsync(ct);
            dto.Localizacion = await LeerLocalizacionAsync(reader, total, ct);

            // Solo cacheamos resultados con datos. TotalTitulares == 0 (manejado arriba
            // con returns tempranos) probablemente sea transitorio y no debemos ocultar
            // la llegada de datos nuevos.
            _cache.Set(carteraID, liderID, dto);
            return dto;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    // ── Helpers ADO.NET ───────────────────────────────────────────────────────

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static async Task<decimal?> GetNullableDecimalAsync(DbDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        return await reader.IsDBNullAsync(ord) ? null : reader.GetDecimal(ord);
    }

    private static async Task<List<DesglosePar>> ReadDesgloseAsync(DbDataReader reader, CancellationToken ct)
    {
        var list = new List<DesglosePar>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DesglosePar
            {
                Etiqueta   = reader.GetString(0),
                Cantidad   = reader.GetInt32(1),
                Porcentaje = reader.GetDecimal(2),
            });
        }
        return list;
    }

    /// <summary>
    /// Lee el result set 9 (una sola fila con 30 SUMs: 15 fuentes × encontrado/no-encontrado)
    /// y construye la lista de LocalizacionFuente. FaltanPorBuscar = total - encontrados - noEncontrados.
    /// Usa GetOrdinal por nombre para mantener consistencia con el resto de la clase.
    /// </summary>
    private static async Task<List<LocalizacionFuente>> LeerLocalizacionAsync(
        DbDataReader reader, int total, CancellationToken ct)
    {
        if (!await reader.ReadAsync(ct)) return [];

        // Si todas las SUMs vienen NULL (sucede con #t vacía aunque ya cubrimos total==0 arriba),
        // tratamos cada columna como 0.
        int Sum(string col)
        {
            var ord = reader.GetOrdinal(col);
            return reader.IsDBNull(ord) ? 0 : reader.GetInt32(ord);
        }

        return
        [
            Fuente("Cajacopi",      Sum("CajacopiEnc"),    Sum("CajacopiNoEnc"),    total),
            Fuente("Cámara",        Sum("CamaraEnc"),      Sum("CamaraNoEnc"),      total),
            Fuente("Salud Total",   Sum("SaludTotalEnc"),  Sum("SaludTotalNoEnc"),  total),
            Fuente("Fosyga",        Sum("FosygaEnc"),      Sum("FosygaNoEnc"),      total),
            Fuente("Compensados",   Sum("CompensadosEnc"), Sum("CompensadosNoEnc"), total),
            Fuente("Positiva",      Sum("PositivaEnc"),    Sum("PositivaNoEnc"),    total),
            Fuente("Tigo",          Sum("TigoEnc"),        Sum("TigoNoEnc"),        total),
            Fuente("SOS",           Sum("SosEnc"),         Sum("SosNoEnc"),         total),
            Fuente("Claro",         Sum("ClaroEnc"),       Sum("ClaroNoEnc"),       total),
            Fuente("MiVacuna",      Sum("MiVacunaEnc"),    Sum("MiVacunaNoEnc"),    total),
            Fuente("MutualSer",     Sum("MutualSerEnc"),   Sum("MutualSerNoEnc"),   total),
            Fuente("NuevaEps",      Sum("NuevaEpsEnc"),    Sum("NuevaEpsNoEnc"),    total),
            Fuente("Sura (ARL)",    Sum("SuraEnc"),        Sum("SuraNoEnc"),        total),
            Fuente("Positiva Emp.", Sum("PosEmpEnc"),      Sum("PosEmpNoEnc"),      total),
            Fuente("RUES",          Sum("RuesEnc"),        Sum("RuesNoEnc"),        total),
            Fuente("SIMIT",         Sum("SimitEnc"),       Sum("SimitNoEnc"),       total),
            Fuente("RGM",           Sum("RgmEnc"),         Sum("RgmNoEnc"),         total),
        ];

        static LocalizacionFuente Fuente(string nombre, int enc, int noEnc, int tot) => new()
        {
            Fuente          = nombre,
            Encontrados     = enc,
            NoEncontrados   = noEnc,
            FaltanPorBuscar = tot - enc - noEnc,
        };
    }

    // ── Descarga CSV de localización ──────────────────────────────────────────
    //
    // Detrás de cada cifra (encontrado / no encontrado / falta por buscar) de la
    // tabla "Porcentajes de Localización" se puede descargar la lista de titulares
    // (Cédula, Nombre, Última Consulta) que clasifican en esa celda. La consulta
    // reusa fn_BaseMadrePorCartera para acotar la cartera por líder y replica
    // exactamente los predicados del result set 9 del batch principal — la única
    // forma de garantizar que el conteo de pantalla coincide con la fila
    // descargada es derivar ambos del mismo predicado.

    private record FuenteMeta(
        string Slug,
        string Etiqueta,        // para nombrar el archivo
        string TablaFuente,     // dbo.Cajacopi, dbo.DatosClaro, etc.
        string ColumnaFecha,    // FechaConsulta o FechaInsercion (DatosClaro)
        string SelectDataCols,  // proyección dentro de la CTE #t
        string PredicadoEncontrado); // condición "encontrado" (alias t.xxx)

    // Metadata de las 15 fuentes — espeja result set 9 de SqlAgregados. El orden
    // y los nombres de alias en SelectDataCols son los mismos que ahí, para que
    // la categorización aquí coincida exactamente con los conteos mostrados.
    private static readonly Dictionary<string, FuenteMeta> Fuentes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cajacopi"] = new("cajacopi", "Cajacopi", "Cajacopi", "FechaConsulta",
            "[Celular Cajacopi] AS CelCajacopi, [Email CajaCopi] AS EmCajacopi, [Edad CajaCopi] AS EdadCajacopi",
            "NULLIF(NULLIF(t.CelCajacopi,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmCajacopi,''),'0') IS NOT NULL " +
            "OR (t.EdadCajacopi IS NOT NULL AND t.EdadCajacopi != 0)"),

        ["camara"] = new("camara", "Camara", "Camara", "FechaConsulta",
            "[Celular Camara] AS CelCamara, [Email Camara] AS EmCamara",
            "NULLIF(NULLIF(t.CelCamara,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmCamara,''),'0') IS NOT NULL"),

        ["salud-total"] = new("salud-total", "SaludTotal", "SaludTotal", "FechaConsulta",
            "[Celular Salud Total] AS CelSaludTotal, [Email Salud Total] AS EmSaludTotal, " +
            "[NIT Salud Total] AS NITSaludTotal, [Empresa Salud Total] AS EmpresaSaludTotal",
            "NULLIF(NULLIF(t.CelSaludTotal,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmSaludTotal,''),'0') IS NOT NULL " +
            "OR (t.NITSaludTotal IS NOT NULL AND t.NITSaludTotal != 0) " +
            "OR NULLIF(NULLIF(t.EmpresaSaludTotal,''),'0') IS NOT NULL"),

        ["fosyga"] = new("fosyga", "Fosyga", "Fosyga", "FechaConsulta",
            "EPS, Regimen, [Tipo Afiliado] AS TipoAfiliado",
            "NULLIF(NULLIF(t.EPS,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.Regimen,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.TipoAfiliado,''),'0') IS NOT NULL"),

        ["compensados"] = new("compensados", "Compensados", "Compensados", "FechaConsulta",
            "[EPS en Compensados] AS EPSCompensados, [Tipo Afiliado Compensados] AS TipoAfiliadoCompensados",
            "NULLIF(NULLIF(t.EPSCompensados,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.TipoAfiliadoCompensados,''),'0') IS NOT NULL"),

        ["positiva"] = new("positiva", "Positiva", "Positiva", "FechaConsulta",
            "[Celular Positiva] AS CelPositiva, [Email Positiva] AS EmPositiva, [Edad Positiva] AS EdadPositiva",
            "NULLIF(NULLIF(t.CelPositiva,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmPositiva,''),'0') IS NOT NULL " +
            "OR (t.EdadPositiva IS NOT NULL AND t.EdadPositiva != 0)"),

        ["tigo"] = new("tigo", "Tigo", "Tigo", "FechaConsulta",
            "[Celular Tigo] AS CelTigo, [Email Tigo] AS EmTigo",
            "NULLIF(NULLIF(t.CelTigo,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmTigo,''),'0') IS NOT NULL"),

        ["sos"] = new("sos", "SOS", "SOS", "FechaConsulta",
            "[Celular SOS] AS CelSOS, [Nit Sos] AS NitSOS, [Empresa SOS] AS EmpresaSOS",
            "NULLIF(NULLIF(t.CelSOS,''),'0') IS NOT NULL " +
            "OR (t.NitSOS IS NOT NULL AND t.NitSOS != 0) " +
            "OR NULLIF(NULLIF(t.EmpresaSOS,''),'0') IS NOT NULL"),

        // DatosClaro es el único que usa FechaInsercion (la tabla no tiene FechaConsulta).
        ["claro"] = new("claro", "Claro", "DatosClaro", "FechaInsercion",
            "[Celular Claro] AS CelClaro, [Email Claro] AS EmClaro",
            "NULLIF(NULLIF(t.CelClaro,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmClaro,''),'0') IS NOT NULL"),

        ["mivacuna"] = new("mivacuna", "MiVacuna", "MiVacuna", "FechaConsulta",
            "[Celular MiVacuna] AS CelMiVacuna, [Email MiVacuna] AS EmMiVacuna",
            "NULLIF(NULLIF(t.CelMiVacuna,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmMiVacuna,''),'0') IS NOT NULL"),

        ["mutualser"] = new("mutualser", "MutualSer", "MutualSer", "FechaConsulta",
            "[Celular MutualSer] AS CelMutualSer, [Celular 2 MutualSer] AS CelMutualSer2, " +
            "[Email MutualSer] AS EmMutualSer, " +
            "[Departamento MutualSer] AS DepartamentoMutualSer, [Municipio MutualSer] AS MunicipioMutualSer",
            "NULLIF(NULLIF(t.CelMutualSer,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.CelMutualSer2,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.EmMutualSer,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.DepartamentoMutualSer,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.MunicipioMutualSer,''),'0') IS NOT NULL"),

        // NuevaEps.TitularID es varchar (todos numericos): la conversion implicita en
        // el JOIN del src CTE es segura.
        ["nuevaeps"] = new("nuevaeps", "NuevaEps", "NuevaEps", "FechaConsulta",
            "[Celular NuevaEps] AS CelNuevaEps, " +
            "[Departamento NuevaEps] AS DepartamentoNuevaEps, [Municipio NuevaEps] AS MunicipioNuevaEps",
            "NULLIF(NULLIF(t.CelNuevaEps,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.DepartamentoNuevaEps,''),'0') IS NOT NULL " +
            "OR NULLIF(NULLIF(t.MunicipioNuevaEps,''),'0') IS NOT NULL"),

        ["sura"] = new("sura", "Sura", "ARLSura", "FechaConsulta",
            "[NIT Sura] AS NITSura, [Empresa Sura] AS EmpresaSura",
            "(t.NITSura IS NOT NULL AND t.NITSura != 0) " +
            "OR NULLIF(NULLIF(t.EmpresaSura,''),'0') IS NOT NULL"),

        ["positiva-empresa"] = new("positiva-empresa", "PositivaEmpresa", "PositivaEmpresas", "FechaConsulta",
            "[NIT Positiva] AS NITPositiva, [Empresa Positiva] AS EmpresaPositiva",
            "(t.NITPositiva IS NOT NULL AND t.NITPositiva != 0) " +
            "OR NULLIF(NULLIF(t.EmpresaPositiva,''),'0') IS NOT NULL"),

        ["rues"] = new("rues", "RUES", "RUES", "FechaConsulta",
            "[Empresa RUES] AS EmpresaRUES",
            "NULLIF(NULLIF(t.EmpresaRUES,''),'0') IS NOT NULL"),

        ["simit"] = new("simit", "SIMIT", "SIMIT", "FechaConsulta",
            "[PLACA SIMIT] AS PlacaSIMIT",
            "NULLIF(NULLIF(t.PlacaSIMIT,''),'0') IS NOT NULL"),

        ["rgm"] = new("rgm", "RGM", "RGM", "FechaConsulta",
            "Marca, Modelo, Placa",
            "NULLIF(NULLIF(t.Marca,''),'0') IS NOT NULL " +
            "OR (t.Modelo IS NOT NULL AND t.Modelo != 0) " +
            "OR NULLIF(NULLIF(t.Placa,''),'0') IS NOT NULL"),
    };

    private static readonly HashSet<string> CategoriasValidas =
        new(StringComparer.OrdinalIgnoreCase) { "encontrado", "no-encontrado", "falta" };

    public async Task EscribirLocalizacionCsvAsync(
        int carteraID, int? liderID,
        string fuenteSlug, string categoria,
        Stream output, CancellationToken ct = default)
    {
        if (!Fuentes.TryGetValue(fuenteSlug ?? string.Empty, out var meta))
            throw new ArgumentException($"Fuente desconocida: {fuenteSlug}", nameof(fuenteSlug));

        if (categoria is null || !CategoriasValidas.Contains(categoria))
            throw new ArgumentException($"Categoría inválida: {categoria}", nameof(categoria));

        // Predicado por categoría:
        //   encontrado    → la cartera tiene datos para esa fuente en este titular.
        //   no-encontrado → no hay datos en la cartera para la fuente, pero el
        //                   titular sí existe en la tabla fuente (se consultó y
        //                   no se halló o quedó vacío).
        //   falta         → no hay datos y el titular tampoco aparece en la tabla
        //                   fuente — todavía no se ha consultado.
        //
        // Existencia se determina con `src.Existe IS NOT NULL` y NO con la fecha
        // — DatosClaro permite FechaInsercion NULL, así que un MAX nulo no
        // implica ausencia de fila (espeja el `dc.TitularID IS NOT NULL` del
        // result set 9 del batch principal).
        var predicado = categoria.ToLowerInvariant() switch
        {
            "encontrado"    => meta.PredicadoEncontrado,
            "no-encontrado" => $"NOT ({meta.PredicadoEncontrado}) AND src.Existe IS NOT NULL",
            "falta"         => $"NOT ({meta.PredicadoEncontrado}) AND src.Existe IS NULL",
            _ => throw new ArgumentException(categoria, nameof(categoria)),
        };

        var sql = $"""
            SET NOCOUNT ON;
            WITH t AS (
                SELECT
                    TRY_CAST([Numero Documento] AS bigint) AS TitularID,
                    {meta.SelectDataCols}
                FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID)
            ),
            src AS (
                SELECT TitularID,
                       MAX({meta.ColumnaFecha}) AS UltimaConsulta,
                       CAST(1 AS bit)           AS Existe
                FROM dbo.{meta.TablaFuente}
                GROUP BY TitularID
            )
            SELECT t.TitularID, ti.Nombre, src.UltimaConsulta
            FROM t
            LEFT JOIN src        ON src.TitularID  = t.TitularID
            LEFT JOIN dbo.Titular ti ON ti.NumeroDocumento = t.TitularID
            WHERE {predicado}
            ORDER BY t.TitularID;
            """;

        _db.Database.SetCommandTimeout(CommandTimeoutSegundos);

        var conn = _db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        // BOM UTF-8 para que Excel ES-CO detecte el encoding con tildes (mismo patrón que Creador).
        await output.WriteAsync(BomUtf8, ct);

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("Cédula;Nombre;Última Consulta");

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText    = sql;
            cmd.CommandTimeout = CommandTimeoutSegundos;
            AddParam(cmd, "@CarteraID", carteraID);
            AddParam(cmd, "@LiderID",   (object?)liderID ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var titularID = reader.GetInt64(0);
                var nombre    = await reader.IsDBNullAsync(1, ct) ? string.Empty : reader.GetString(1);
                var fecha     = await reader.IsDBNullAsync(2, ct) ? (DateTime?)null : reader.GetDateTime(2);

                await writer.WriteAsync(titularID.ToString(CultureInfo.InvariantCulture));
                await writer.WriteAsync(';');
                await writer.WriteAsync(EscaparCsv(nombre));
                await writer.WriteAsync(';');
                if (fecha.HasValue)
                    await writer.WriteAsync(fecha.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                await writer.WriteAsync('\n');
            }

            await writer.FlushAsync(ct);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    public async Task<string> ResolverNombreArchivoLocalizacionAsync(
        int carteraID, int? liderID, string fuenteSlug, string categoria)
    {
        if (!Fuentes.TryGetValue(fuenteSlug ?? string.Empty, out var meta))
            throw new ArgumentException($"Fuente desconocida: {fuenteSlug}", nameof(fuenteSlug));

        var nombreCartera = await _db.Carteras
            .Where(c => c.ID == carteraID)
            .Select(c => c.NombreCartera)
            .FirstOrDefaultAsync() ?? $"Cartera{carteraID}";

        var nombreLider = liderID.HasValue
            ? (await _db.Lideres.Where(l => l.ID == liderID.Value).Select(l => l.Nombre).FirstOrDefaultAsync()
               ?? $"Lider{liderID.Value}")
            : "Completo";

        return $"Localizacion_{Sanitizar(nombreCartera)}_{Sanitizar(nombreLider)}_{meta.Etiqueta}_{categoria}_{DateTime.Today:yyyyMMdd}.csv";
    }

    private static readonly byte[] BomUtf8 = [0xEF, 0xBB, 0xBF];

    private static string EscaparCsv(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return string.Empty;
        var requiereQuoting = valor.IndexOfAny([';', '"', '\n', '\r']) >= 0;
        if (!requiereQuoting) return valor;
        return $"\"{valor.Replace("\"", "\"\"")}\"";
    }

    private static string Sanitizar(string nombre) =>
        string.Concat(nombre.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
}

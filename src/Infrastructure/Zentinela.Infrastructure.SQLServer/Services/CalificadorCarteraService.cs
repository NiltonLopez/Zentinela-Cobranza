using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Calcula las métricas del Calificador de Cartera. Toda la agregación vive en SQL:
/// un único batch materializa la cartera filtrada en una temporal y emite 8 result
/// sets (escalares + 7 desgloses). C# sólo hidrata el DTO. Reemplaza los 9 bloques
/// C# en memoria que iteraban sobre 60-190k filas.
/// </summary>
public class CalificadorCarteraService : ICalificadorCarteraService
{
    private readonly CallCenterDbContext _db;

    public CalificadorCarteraService(CallCenterDbContext db)
    {
        _db = db;
    }

    // ── Dropdown ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(
        ContextoUsuario contexto)
    {
        if (contexto.Rol == Roles.Lider && contexto.LiderID.HasValue)
        {
            // Líder: solo sus combinaciones expuestas en vw_BaseMadre
            var lid = contexto.LiderID.Value;
            return await _db.BaseMadreRows
                .Where(b => b.LiderID == lid)
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

        // Director / Admin: todas las asignaciones activas de Lider_Cartera
        return await _db.LiderCarteras
            .Select(lc => new CarteraLiderDropdownDto
            {
                CarteraID = lc.CarteraID,
                LiderID   = lc.LiderID,
                Etiqueta  = lc.Cartera.NombreCartera + " — " + lc.Lider.Nombre,
            })
            .OrderBy(o => o.Etiqueta)
            .AsNoTracking()
            .ToListAsync();
    }

    // ── Métricas ──────────────────────────────────────────────────────────────

    /// <summary>
    /// SQL batch que materializa la cartera en #t (con columnas pre-parseadas) y
    /// emite 8 result sets. Reemplaza los 9 bloques C# que iteraban filas en memoria.
    /// Los nombres de columna y orden de filas replican exactamente lo que producía
    /// el código C# (Cantidad DESC, OrdenRango para Rotación, top 10 + Otras para EPS).
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
            [Email CajaCopi]                  AS EmCajacopi,
            [Email Camara]                    AS EmCamara,
            [Email Salud Total]               AS EmSaludTotal,
            [Email Positiva]                  AS EmPositiva,
            [Email Tigo]                      AS EmTigo,
            [Email Claro]                     AS EmClaro,
            [Email MutualSer]                 AS EmMutualSer,
            [Edad Registraduria]              AS EdadReg,
            TRY_CAST([Saldo]                       AS decimal(18,2))  AS _Saldo,
            TRY_CAST(NULLIF([Valor Promesa], '0')  AS decimal(18,2))  AS _ValorPromesa,
            TRY_CAST([Rotacion]                    AS int)            AS _Rotacion,
            TRY_CONVERT(date, [Fecha Promesa], 103)                   AS _FechaPromesa
        INTO #t
        FROM dbo.fn_BaseMadrePorCartera(@CarteraID, @LiderID);

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
                   OR CelMutualSer2 NOT IN ('','0'))                                AS ConCelular,
            (SELECT COUNT(*) FROM #t
                WHERE EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                   OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                   OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                   OR EmMutualSer  NOT IN ('','0'))                                 AS ConEmail,
            (SELECT COUNT(*) FROM #t
                WHERE (CelCajacopi   NOT IN ('','0') OR CelCamara    NOT IN ('','0')
                    OR CelSaludTotal NOT IN ('','0') OR CelPositiva  NOT IN ('','0')
                    OR CelTigo       NOT IN ('','0') OR CelSOS       NOT IN ('','0')
                    OR CelClaro      NOT IN ('','0') OR CelMutualSer NOT IN ('','0')
                    OR CelMutualSer2 NOT IN ('','0'))
                  AND (EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                    OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                    OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                    OR EmMutualSer  NOT IN ('','0'))) AS ConCelularYEmail,
            (SELECT COUNT(*) FROM #t
                WHERE NOT (CelCajacopi   NOT IN ('','0') OR CelCamara    NOT IN ('','0')
                        OR CelSaludTotal NOT IN ('','0') OR CelPositiva  NOT IN ('','0')
                        OR CelTigo       NOT IN ('','0') OR CelSOS       NOT IN ('','0')
                        OR CelClaro      NOT IN ('','0') OR CelMutualSer NOT IN ('','0')
                        OR CelMutualSer2 NOT IN ('','0'))
                  AND NOT (EmCajacopi   NOT IN ('','0') OR EmCamara   NOT IN ('','0')
                        OR EmSaludTotal NOT IN ('','0') OR EmPositiva NOT IN ('','0')
                        OR EmTigo       NOT IN ('','0') OR EmClaro    NOT IN ('','0')
                        OR EmMutualSer  NOT IN ('','0'))) AS SinContacto,
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
        )
        SELECT Fuente AS Etiqueta, Cantidad,
               CAST(ROUND(CAST(Cantidad AS decimal(18,4)) / NULLIF(@Total, 0) * 100, 1) AS decimal(10,1)) AS Porcentaje
        FROM F WHERE Cantidad > 0 ORDER BY Cantidad DESC;

        DROP TABLE #t;
        """;

    public async Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID)
    {
        var conn = _db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            var dto = new CalificadorCarteraDto();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = SqlAgregados;
                AddParam(cmd, "@CarteraID", carteraID);
                AddParam(cmd, "@LiderID",   liderID);

                await using var reader = await cmd.ExecuteReaderAsync();

                // Result set 1 — escalares
                if (!await reader.ReadAsync())
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
                await reader.NextResultAsync(); dto.DesgloseRotacion       = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesglosePerfil         = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesgloseTipoAfiliado   = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesgloseRegimen        = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesgloseEPS            = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesgloseDepartamento   = await ReadDesgloseAsync(reader);
                await reader.NextResultAsync(); dto.DesgloseFuentesCelular = await ReadDesgloseAsync(reader);
            } // reader y command cerrados, conexión libre para la siguiente query

            // Localización: query separada (ya optimizada vía TVF en commit 95ea28a).
            // Mantenerla aparte simplifica el batch y no agrega latencia significativa.
            dto.Localizacion = await ObtenerLocalizacionAsync(carteraID, liderID, dto.TotalTitulares);

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

    private static async Task<List<DesglosePar>> ReadDesgloseAsync(DbDataReader reader)
    {
        var list = new List<DesglosePar>();
        while (await reader.ReadAsync())
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
    /// Calcula los conteos de localización por fuente (encontrado / no encontrado /
    /// falta buscar). Usa el TVF dbo.fn_BaseMadrePorCartera como base + 14 LEFT JOIN
    /// DISTINCT contra cada tabla fuente para distinguir "no encontrado" (existe fila)
    /// de "falta buscar" (no existe fila).
    /// </summary>
    private async Task<List<LocalizacionFuente>> ObtenerLocalizacionAsync(
        int carteraID, int liderID, int total)
    {
        var r = await _db.Database.SqlQuery<LocalizacionResumenRaw>($"""
            SELECT
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCajacopi,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailCajacopi,  ''), '0') IS NOT NULL
                           OR (b.EdadCajacopi IS NOT NULL AND b.EdadCajacopi != 0)
                         THEN 1 ELSE 0 END) AS CajacopiEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCajacopi,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailCajacopi,  ''), '0') IS NULL
                          AND (b.EdadCajacopi IS NULL OR b.EdadCajacopi = 0)
                          AND b.EnCajacopi = 1
                         THEN 1 ELSE 0 END) AS CajacopiNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCamara,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailCamara,  ''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS CamaraEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularCamara,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailCamara,  ''), '0') IS NULL
                          AND b.EnCamara = 1
                         THEN 1 ELSE 0 END) AS CamaraNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSaludTotal,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailSaludTotal,  ''), '0') IS NOT NULL
                           OR (b.NITSaludTotal IS NOT NULL AND b.NITSaludTotal != 0)
                           OR NULLIF(NULLIF(b.EmpresaSaludTotal,''),'0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SaludTotalEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSaludTotal,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailSaludTotal,  ''), '0') IS NULL
                          AND (b.NITSaludTotal IS NULL OR b.NITSaludTotal = 0)
                          AND NULLIF(NULLIF(b.EmpresaSaludTotal,''),'0') IS NULL
                          AND b.EnSaludTotal = 1
                         THEN 1 ELSE 0 END) AS SaludTotalNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPS,         ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.Regimen,     ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.TipoAfiliado,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS FosygaEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPS,         ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.Regimen,     ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.TipoAfiliado,''), '0') IS NULL
                          AND b.EnFosyga = 1
                         THEN 1 ELSE 0 END) AS FosygaNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPSCompensados,         ''), '0') IS NOT NULL
                           OR NULLIF(NULLIF(b.TipoAfiliadoCompensados,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS CompensadosEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EPSCompensados,         ''), '0') IS NULL
                          AND NULLIF(NULLIF(b.TipoAfiliadoCompensados,''), '0') IS NULL
                          AND b.EnCompensados = 1
                         THEN 1 ELSE 0 END) AS CompensadosNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularPositiva,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailPositiva,  ''), '0') IS NOT NULL
                           OR (b.EdadPositiva IS NOT NULL AND b.EdadPositiva != 0)
                         THEN 1 ELSE 0 END) AS PositivaEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularPositiva,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailPositiva,  ''), '0') IS NULL
                          AND (b.EdadPositiva IS NULL OR b.EdadPositiva = 0)
                          AND b.EnPositiva = 1
                         THEN 1 ELSE 0 END) AS PositivaNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularTigo,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailTigo,  ''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS TigoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularTigo,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailTigo,  ''), '0') IS NULL
                          AND b.EnTigo = 1
                         THEN 1 ELSE 0 END) AS TigoNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSOS,  ''), '0') IS NOT NULL
                           OR (b.NitSOS IS NOT NULL AND b.NitSOS != 0)
                           OR NULLIF(NULLIF(b.EmpresaSOS,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SosEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularSOS,  ''), '0') IS NULL
                          AND (b.NitSOS IS NULL OR b.NitSOS = 0)
                          AND NULLIF(NULLIF(b.EmpresaSOS,''), '0') IS NULL
                          AND b.EnSOS = 1
                         THEN 1 ELSE 0 END) AS SosNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularClaro,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailClaro,  ''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS ClaroEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularClaro,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailClaro,  ''), '0') IS NULL
                          AND b.EnClaro = 1
                         THEN 1 ELSE 0 END) AS ClaroNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularMutualSer, ''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.Celular2MutualSer,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.EmailMutualSer,   ''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.DepartamentoMutualSer,''),'0') IS NOT NULL
                           OR NULLIF(NULLIF(b.MunicipioMutualSer,   ''),'0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS MutualSerEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.CelularMutualSer, ''),'0') IS NULL
                          AND NULLIF(NULLIF(b.Celular2MutualSer,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.EmailMutualSer,   ''),'0') IS NULL
                          AND NULLIF(NULLIF(b.DepartamentoMutualSer,''),'0') IS NULL
                          AND NULLIF(NULLIF(b.MunicipioMutualSer,   ''),'0') IS NULL
                          AND b.EnMutualSer = 1
                         THEN 1 ELSE 0 END) AS MutualSerNoEnc,
                SUM(CASE WHEN (b.NITSura IS NOT NULL AND b.NITSura != 0)
                           OR NULLIF(NULLIF(b.EmpresaSura,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SuraEnc,
                SUM(CASE WHEN (b.NITSura IS NULL OR b.NITSura = 0)
                          AND NULLIF(NULLIF(b.EmpresaSura,''), '0') IS NULL
                          AND b.EnSura = 1
                         THEN 1 ELSE 0 END) AS SuraNoEnc,
                SUM(CASE WHEN (b.NITPositiva IS NOT NULL AND b.NITPositiva != 0)
                           OR NULLIF(NULLIF(b.EmpresaPositiva,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS PosEmpEnc,
                SUM(CASE WHEN (b.NITPositiva IS NULL OR b.NITPositiva = 0)
                          AND NULLIF(NULLIF(b.EmpresaPositiva,''), '0') IS NULL
                          AND b.EnPosEmp = 1
                         THEN 1 ELSE 0 END) AS PosEmpNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EmpresaRUES,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS RuesEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.EmpresaRUES,''), '0') IS NULL
                          AND b.EnRUES = 1
                         THEN 1 ELSE 0 END) AS RuesNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.PlacaSIMIT,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS SimitEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.PlacaSIMIT,''), '0') IS NULL
                          AND b.EnSIMIT = 1
                         THEN 1 ELSE 0 END) AS SimitNoEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.Marca,''), '0') IS NOT NULL
                           OR (b.Modelo IS NOT NULL AND b.Modelo != 0)
                           OR NULLIF(NULLIF(b.Placa,''), '0') IS NOT NULL
                         THEN 1 ELSE 0 END) AS RgmEnc,
                SUM(CASE WHEN NULLIF(NULLIF(b.Marca,''), '0') IS NULL
                          AND (b.Modelo IS NULL OR b.Modelo = 0)
                          AND NULLIF(NULLIF(b.Placa,''), '0') IS NULL
                          AND b.EnRGM = 1
                         THEN 1 ELSE 0 END) AS RgmNoEnc
            FROM (
                SELECT
                    TRY_CAST(v.[Numero Documento] AS bigint) AS TitularID,
                    v.[Celular Cajacopi]          AS CelularCajacopi,
                    v.[Email CajaCopi]            AS EmailCajacopi,
                    v.[Edad CajaCopi]             AS EdadCajacopi,
                    v.[Celular Camara]            AS CelularCamara,
                    v.[Email Camara]              AS EmailCamara,
                    v.[Celular Salud Total]       AS CelularSaludTotal,
                    v.[Email Salud Total]         AS EmailSaludTotal,
                    v.[NIT Salud Total]           AS NITSaludTotal,
                    v.[Empresa Salud Total]       AS EmpresaSaludTotal,
                    v.EPS,
                    v.Regimen,
                    v.[Tipo Afiliado]             AS TipoAfiliado,
                    v.[EPS en Compensados]        AS EPSCompensados,
                    v.[Tipo Afiliado Compensados] AS TipoAfiliadoCompensados,
                    v.[Ultimo Periodo Compensado] AS UltimoPeriodoCompensado,
                    v.[Celular Positiva]          AS CelularPositiva,
                    v.[Email Positiva]            AS EmailPositiva,
                    v.[Edad Positiva]             AS EdadPositiva,
                    v.[Celular Tigo]              AS CelularTigo,
                    v.[Email Tigo]                AS EmailTigo,
                    v.[Celular SOS]               AS CelularSOS,
                    v.[Nit Sos]                   AS NitSOS,
                    v.[Empresa SOS]               AS EmpresaSOS,
                    v.[Celular Claro]             AS CelularClaro,
                    v.[Email Claro]               AS EmailClaro,
                    v.[Celular MutualSer]         AS CelularMutualSer,
                    v.[Celular 2 MutualSer]       AS Celular2MutualSer,
                    v.[Email MutualSer]           AS EmailMutualSer,
                    v.[Departamento MutualSer]    AS DepartamentoMutualSer,
                    v.[Municipio MutualSer]       AS MunicipioMutualSer,
                    v.[NIT Sura]                  AS NITSura,
                    v.[Empresa Sura]              AS EmpresaSura,
                    v.[NIT Positiva]              AS NITPositiva,
                    v.[Empresa Positiva]          AS EmpresaPositiva,
                    v.[Empresa RUES]              AS EmpresaRUES,
                    v.[PLACA SIMIT]               AS PlacaSIMIT,
                    v.Marca,
                    v.Modelo,
                    v.Placa,
                    CASE WHEN cj.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCajacopi,
                    CASE WHEN cm.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCamara,
                    CASE WHEN st.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSaludTotal,
                    CASE WHEN f.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnFosyga,
                    CASE WHEN co.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnCompensados,
                    CASE WHEN p.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnPositiva,
                    CASE WHEN ti.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnTigo,
                    CASE WHEN s.TitularID  IS NOT NULL THEN 1 ELSE 0 END AS EnSOS,
                    CASE WHEN dc.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnClaro,
                    CASE WHEN ms.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnMutualSer,
                    CASE WHEN ar.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSura,
                    CASE WHEN pe.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnPosEmp,
                    CASE WHEN ru.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnRUES,
                    CASE WHEN si.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnSIMIT,
                    CASE WHEN rg.TitularID IS NOT NULL THEN 1 ELSE 0 END AS EnRGM
                FROM dbo.fn_BaseMadrePorCartera({carteraID}, {liderID}) v
                LEFT JOIN (SELECT DISTINCT TitularID FROM Cajacopi)         cj ON cj.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Camara)           cm ON cm.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SaludTotal)       st ON st.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Fosyga)           f  ON f.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Compensados)      co ON co.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Positiva)         p  ON p.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM Tigo)             ti ON ti.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SOS)              s  ON s.TitularID  = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM DatosClaro)       dc ON dc.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM MutualSer)        ms ON ms.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM ARLSura)          ar ON ar.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM PositivaEmpresas) pe ON pe.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM RUES)             ru ON ru.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM SIMIT)            si ON si.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
                LEFT JOIN (SELECT DISTINCT TitularID FROM RGM)              rg ON rg.TitularID = TRY_CAST(v.[Numero Documento] AS bigint)
            ) b
            """).FirstOrDefaultAsync();

        if (r is null) return [];

        return
        [
            Fuente("Cajacopi",       r.CajacopiEnc,    r.CajacopiNoEnc,    total),
            Fuente("Cámara",         r.CamaraEnc,      r.CamaraNoEnc,      total),
            Fuente("Salud Total",    r.SaludTotalEnc,  r.SaludTotalNoEnc,  total),
            Fuente("Fosyga",         r.FosygaEnc,      r.FosygaNoEnc,      total),
            Fuente("Compensados",    r.CompensadosEnc, r.CompensadosNoEnc, total),
            Fuente("Positiva",       r.PositivaEnc,    r.PositivaNoEnc,    total),
            Fuente("Tigo",           r.TigoEnc,        r.TigoNoEnc,        total),
            Fuente("SOS",            r.SosEnc,         r.SosNoEnc,         total),
            Fuente("Claro",          r.ClaroEnc,       r.ClaroNoEnc,       total),
            Fuente("MutualSer",      r.MutualSerEnc,   r.MutualSerNoEnc,   total),
            Fuente("Sura (ARL)",     r.SuraEnc,        r.SuraNoEnc,        total),
            Fuente("Positiva Emp.",  r.PosEmpEnc,      r.PosEmpNoEnc,      total),
            Fuente("RUES",           r.RuesEnc,        r.RuesNoEnc,        total),
            Fuente("SIMIT",          r.SimitEnc,       r.SimitNoEnc,       total),
            Fuente("RGM",            r.RgmEnc,         r.RgmNoEnc,         total),
        ];

        static LocalizacionFuente Fuente(string nombre, int enc, int noEnc, int tot) => new()
        {
            Fuente          = nombre,
            Encontrados     = enc,
            NoEncontrados   = noEnc,
            FaltanPorBuscar = tot - enc - noEnc,
        };
    }

    // DTO interno para mapear el resultado del SQL de localización
    private sealed class LocalizacionResumenRaw
    {
        public int CajacopiEnc    { get; set; } public int CajacopiNoEnc    { get; set; }
        public int CamaraEnc      { get; set; } public int CamaraNoEnc      { get; set; }
        public int SaludTotalEnc  { get; set; } public int SaludTotalNoEnc  { get; set; }
        public int FosygaEnc      { get; set; } public int FosygaNoEnc      { get; set; }
        public int CompensadosEnc { get; set; } public int CompensadosNoEnc { get; set; }
        public int PositivaEnc    { get; set; } public int PositivaNoEnc    { get; set; }
        public int TigoEnc        { get; set; } public int TigoNoEnc        { get; set; }
        public int SosEnc         { get; set; } public int SosNoEnc         { get; set; }
        public int ClaroEnc       { get; set; } public int ClaroNoEnc       { get; set; }
        public int MutualSerEnc   { get; set; } public int MutualSerNoEnc   { get; set; }
        public int SuraEnc        { get; set; } public int SuraNoEnc        { get; set; }
        public int PosEmpEnc      { get; set; } public int PosEmpNoEnc      { get; set; }
        public int RuesEnc        { get; set; } public int RuesNoEnc        { get; set; }
        public int SimitEnc       { get; set; } public int SimitNoEnc       { get; set; }
        public int RgmEnc         { get; set; } public int RgmNoEnc         { get; set; }
    }
}

using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.IVR;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Informe de IVR (solo lectura) sobre la tabla unificada HistoricoVirtualidad
/// (Canal = 'IVR'). El alta de resultados vive en <see cref="HistoricoVirtualidadService"/>
/// (módulo Administración).
/// </summary>
public class VirtualidadIVRService : IVirtualidadIVRService
{
    private const string CANAL = "IVR";
    private const int UMBRAL_ALERTA = 5; // >5 IVR a la misma línea

    private readonly CallCenterDbContext _db;
    private readonly IExcelExporter      _excel;

    public VirtualidadIVRService(CallCenterDbContext db, IExcelExporter excel)
    {
        _db    = db;
        _excel = excel;
    }

    public async Task<InformeIVRDto> GetInformeAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        var queryEnriquecida =
            from s in _db.HistoricoVirtualidad
            where s.Canal == CANAL
                && s.FechaEnvio >= desde && s.FechaEnvio <= hasta
                && (!liderID.HasValue   || s.LiderID   == liderID.Value)
                && (!carteraID.HasValue || s.CarteraID == carteraID.Value)
            join c in _db.Carteras on s.CarteraID equals c.ID
            join l in _db.Lideres  on s.LiderID   equals l.ID
            select new
            {
                s.LiderID,
                NombreLider   = l.Nombre,
                s.CarteraID,
                c.NombreCartera,
                NumeroTelefono = s.Destino,
            };

        var porLiderCartera = await queryEnriquecida
            .GroupBy(x => new { x.LiderID, x.NombreLider, x.CarteraID, x.NombreCartera })
            .Select(g => new IVRPorLiderCartera
            {
                LiderID       = g.Key.LiderID,
                NombreLider   = g.Key.NombreLider,
                CarteraID     = g.Key.CarteraID,
                NombreCartera = g.Key.NombreCartera,
                Total         = g.Count(),
            })
            .OrderByDescending(g => g.Total)
            .ToListAsync();

        var alertas = await queryEnriquecida
            .GroupBy(x => new { x.NumeroTelefono, x.NombreCartera })
            .Where(g => g.Count() > UMBRAL_ALERTA)
            .Select(g => new AlertaIVRDto
            {
                NumeroTelefono = g.Key.NumeroTelefono,
                NombreCartera  = g.Key.NombreCartera,
                TotalIVR       = g.Count(),
            })
            .OrderByDescending(a => a.TotalIVR)
            .ToListAsync();

        return new InformeIVRDto
        {
            TotalIVR        = porLiderCartera.Sum(p => p.Total),
            PorLiderCartera = porLiderCartera,
            Alertas         = alertas,
        };
    }

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║  Análisis IVR — embudo y desenlaces (ADR-031)                         ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    public async Task<AnalisisIvrDto> GetAnalisisAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        var celdas = await ConsultarCeldasAsync(desde, hasta, liderID, carteraID);

        var dto = new AnalisisIvrDto
        {
            Desde            = desde,
            Hasta            = hasta,
            TotalMarcaciones = celdas.Sum(c => c.Marcaciones),
        };

        if (celdas.Count == 0)
        {
            dto.Apreciacion = "No hay resultados de IVR cargados para el período seleccionado.";
            return dto;
        }

        // Nombres: las tablas de líderes y carteras son pequeñas, un lookup basta.
        var liderIDs   = celdas.Select(c => c.LiderID).Distinct().ToList();
        var carteraIDs = celdas.Select(c => c.CarteraID).Distinct().ToList();

        var nombreLider = await _db.Lideres
            .Where(l => liderIDs.Contains(l.ID))
            .ToDictionaryAsync(l => l.ID, l => l.Nombre);
        var nombreCartera = await _db.Carteras
            .Where(c => carteraIDs.Contains(c.ID))
            .ToDictionaryAsync(c => c.ID, c => c.NombreCartera);

        // ── Global ───────────────────────────────────────────────────────────
        var m = Calcular(celdas);
        dto.ClientesMarcados     = m.Marcados;
        dto.SinResultado         = m.SinResultado;
        dto.ClientesConResultado = m.ConResultado;
        dto.Contactados          = m.Contactados;
        dto.EscucharonMenu       = m.Menu;
        dto.MarcaronOpcion1      = m.Opcion1;
        dto.Contactabilidad      = m.Contactabilidad;
        dto.ConversionOpcion1    = m.ConversionOpcion1;
        dto.TotalLideres  = liderIDs.Count;
        dto.TotalCarteras = carteraIDs.Count;
        dto.Embudo        = ConstruirEmbudo(m.ConResultado, m.Contactados, m.Menu, m.Opcion1);
        dto.Desenlaces    = ConstruirDesenlaces(celdas, m.Contactados);
        dto.Apreciacion   = ApreciacionGlobal(dto);

        // ── Por líder ────────────────────────────────────────────────────────
        dto.PorLider = celdas
            .GroupBy(c => c.LiderID)
            .Select(g =>
            {
                var delLider = g.ToList();
                var lider = new AnalisisIvrLiderDto
                {
                    LiderID       = g.Key,
                    NombreLider   = nombreLider.TryGetValue(g.Key, out var n) ? n : $"Líder {g.Key}",
                    TotalCarteras = delLider.Select(c => c.CarteraID).Distinct().Count(),
                };
                var ml = Calcular(delLider);
                lider.ClientesMarcados     = ml.Marcados;
                lider.SinResultado         = ml.SinResultado;
                lider.ClientesConResultado = ml.ConResultado;
                lider.Contactados          = ml.Contactados;
                lider.EscucharonMenu       = ml.Menu;
                lider.MarcaronOpcion1      = ml.Opcion1;
                lider.Contactabilidad      = ml.Contactabilidad;
                lider.ConversionOpcion1    = ml.ConversionOpcion1;
                lider.Embudo     = ConstruirEmbudo(ml.ConResultado, ml.Contactados, ml.Menu, ml.Opcion1);
                lider.Desenlaces = ConstruirDesenlaces(delLider, ml.Contactados);

                lider.PorCartera = delLider
                    .GroupBy(c => c.CarteraID)
                    .Select(gc =>
                    {
                        var mc = Calcular(gc.ToList());
                        return new AnalisisIvrCarteraDto
                        {
                            CarteraID            = gc.Key,
                            NombreCartera        = nombreCartera.TryGetValue(gc.Key, out var nc) ? nc : $"Cartera {gc.Key}",
                            ClientesMarcados     = mc.Marcados,
                            SinResultado         = mc.SinResultado,
                            ClientesConResultado = mc.ConResultado,
                            Contactados          = mc.Contactados,
                            MarcaronOpcion1      = mc.Opcion1,
                            Contactabilidad      = mc.Contactabilidad,
                        };
                    })
                    .OrderByDescending(c => c.ClientesMarcados)
                    .ToList();

                return lider;
            })
            .OrderByDescending(l => l.ClientesMarcados)
            .ToList();

        foreach (var l in dto.PorLider)
            l.Apreciacion = ApreciacionLider(l, dto);

        return dto;
    }

    /// <summary>
    /// Celda del cubo: por (líder, cartera, rango de desenlace), cuántos CLIENTES ÚNICOS
    /// terminaron ahí. De acá se derivan todos los niveles del informe sin volver a la BD.
    /// </summary>
    private sealed record CeldaIvr(int LiderID, int CarteraID, int Rango, int Clientes, int Marcaciones);

    /// <summary>
    /// Estado de una marcación -> rango de desenlace (ver <see cref="CatalogoVirtualidad.DesenlacesIvr"/>).
    /// Lo usan el análisis y el Excel: si divergieran, el detalle no cuadraría con el resumen.
    /// CASE "searched", no "simple": un CASE simple nunca matchea NULL y mandaría las filas
    /// sin resultado al mismo bucket que las fallas técnicas.
    /// </summary>
    private const string RangoSql = """
        CASE
          WHEN h.Estado IS NULL               THEN 98
          WHEN h.Estado = 'OPCION 1'          THEN 1
          WHEN h.Estado = 'ESPERAR OPCION'    THEN 2
          WHEN h.Estado = 'OPCION INCORRECTA' THEN 3
          WHEN h.Estado = 'REPRODUCIR'        THEN 4
          WHEN h.Estado = 'ESCUCHANDO'        THEN 4
          WHEN h.Estado = 'COLGAR'            THEN 5
          ELSE 99
        END
        """;

    // ── Excel para el líder ─────────────────────────────────────────────────

    public async Task<byte[]> ExportarExcelAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        // El resumen es el MISMO análisis del panel (no un cálculo aparte), y el detalle
        // usa la misma regla de desenlace (RangoSql): las dos hojas cuadran entre sí.
        var analisis = await GetAnalisisAsync(desde, hasta, liderID, carteraID);
        var detalle  = await ConsultarDetalleAsync(desde, hasta, liderID, carteraID);

        var partes = new List<string>();
        if (liderID.HasValue)
        {
            var lider = await _db.Lideres.Where(l => l.ID == liderID.Value).Select(l => l.Nombre).FirstOrDefaultAsync();
            partes.Add($"Líder {lider ?? liderID.Value.ToString()}");
        }
        if (carteraID.HasValue)
        {
            var cartera = await _db.Carteras.Where(c => c.ID == carteraID.Value).Select(c => c.NombreCartera).FirstOrDefaultAsync();
            partes.Add($"Cartera {cartera ?? carteraID.Value.ToString()}");
        }
        var alcance = partes.Count > 0 ? string.Join(" · ", partes) : "Todos los líderes y carteras";

        var pagosHasta = await _db.Pagos.MaxAsync(p => (DateOnly?)p.FechaPago);

        return _excel.GenerarInformeIvr(analisis, detalle, alcance, pagosHasta);
    }

    /// <summary>
    /// Una fila por cliente marcado (líder, cartera, documento) — la misma unidad del análisis.
    /// Todo en UNA consulta con agregados: con ~120k clientes por mes, una consulta por
    /// cliente sería impracticable.
    ///
    /// Pagos posteriores: pagos a ESA cartera desde la primera marcación del período hasta
    /// hoy. Es por cartera a propósito: un pago a otra cartera no mide esta estrategia.
    /// </summary>
    private async Task<List<DetalleClienteIvrDto>> ConsultarDetalleAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        var sql = new StringBuilder($"""
            ;WITH Base AS (
                SELECT h.ID, h.LiderID, h.CarteraID, h.NumeroDocumento, h.Destino, h.FechaEnvio, h.Estado,
                       {RangoSql} AS Rango
                  FROM dbo.HistoricoVirtualidad h
                 WHERE h.Canal = @canal
                   AND h.FechaEnvio >= @desde AND h.FechaEnvio <= @hasta
            """);
        sql.AppendLine();
        if (liderID.HasValue)   sql.AppendLine("       AND h.LiderID = @liderID");
        if (carteraID.HasValue) sql.AppendLine("       AND h.CarteraID = @carteraID");
        sql.AppendLine("""
            ),
            Cliente AS (
                SELECT LiderID, CarteraID, NumeroDocumento,
                       MIN(Rango) AS Rango, COUNT(*) AS Marcaciones,
                       MIN(FechaEnvio) AS Primera, MAX(FechaEnvio) AS Ultima
                  FROM Base
                 GROUP BY LiderID, CarteraID, NumeroDocumento
            ),
            Ultimo AS (
                SELECT LiderID, CarteraID, NumeroDocumento, Estado,
                       ROW_NUMBER() OVER (PARTITION BY LiderID, CarteraID, NumeroDocumento
                                          ORDER BY FechaEnvio DESC, ID DESC) AS rn
                  FROM Base
            ),
            Tel AS (
                SELECT LiderID, CarteraID, NumeroDocumento,
                       STRING_AGG(CAST(Destino AS VARCHAR(MAX)), ', ') WITHIN GROUP (ORDER BY Destino) AS Telefonos
                  FROM (SELECT DISTINCT LiderID, CarteraID, NumeroDocumento, Destino
                          FROM Base WHERE Destino IS NOT NULL) d
                 GROUP BY LiderID, CarteraID, NumeroDocumento
            ),
            Pag AS (
                SELECT c.LiderID, c.CarteraID, c.NumeroDocumento,
                       COUNT(*) AS Pagos, SUM(p.ValorPagado) AS Valor, MAX(p.FechaPago) AS UltimoPago
                  FROM Cliente c
                  JOIN dbo.Pago p ON p.CedulaTitular = c.NumeroDocumento
                                 AND p.CarteraID     = c.CarteraID
                                 AND p.FechaPago    >= c.Primera
                 GROUP BY c.LiderID, c.CarteraID, c.NumeroDocumento
            )
            SELECT c.NumeroDocumento, t.Nombre, l.Nombre, ca.NombreCartera, tel.Telefonos,
                   c.Marcaciones, c.Primera, c.Ultima, c.Rango, u.Estado,
                   e.UltimoPerfil, e.MejorPerfil, e.FechaUltimaGestion, e.FechaPromesa,
                   CAST(e.ValorPromesa AS DECIMAL(19,4)), CAST(e.SaldoTotal AS DECIMAL(19,4)),
                   ISNULL(pg.Pagos, 0), CAST(ISNULL(pg.Valor, 0) AS DECIMAL(19,4)), pg.UltimoPago,
                   c.LiderID, c.CarteraID
              FROM Cliente c
              LEFT JOIN dbo.Lider   l  ON l.ID  = c.LiderID
              LEFT JOIN dbo.Cartera ca ON ca.ID = c.CarteraID
              LEFT JOIN Ultimo u  ON u.LiderID = c.LiderID AND u.CarteraID = c.CarteraID
                                 AND u.NumeroDocumento = c.NumeroDocumento AND u.rn = 1
              LEFT JOIN Tel tel   ON tel.LiderID = c.LiderID AND tel.CarteraID = c.CarteraID
                                 AND tel.NumeroDocumento = c.NumeroDocumento
              LEFT JOIN Pag pg    ON pg.LiderID = c.LiderID AND pg.CarteraID = c.CarteraID
                                 AND pg.NumeroDocumento = c.NumeroDocumento
              LEFT JOIN dbo.UltimoEstadoTitular e ON e.TitularID = c.NumeroDocumento AND e.CarteraID = c.CarteraID
              -- TOP 1: si Titular repitiera documento (p. ej. por empresa), no duplica al cliente.
              OUTER APPLY (SELECT TOP (1) ti.Nombre FROM dbo.Titular ti
                            WHERE ti.NumeroDocumento = c.NumeroDocumento) t
             ORDER BY l.Nombre, ca.NombreCartera, c.Rango, c.NumeroDocumento;
            """);

        var parametros = new List<SqlParameter>
        {
            new("@canal", CANAL),
            new("@desde", desde.ToDateTime(TimeOnly.MinValue)),
            new("@hasta", hasta.ToDateTime(TimeOnly.MinValue)),
        };
        if (liderID.HasValue)   parametros.Add(new SqlParameter("@liderID", liderID.Value));
        if (carteraID.HasValue) parametros.Add(new SqlParameter("@carteraID", carteraID.Value));

        var filas = new List<DetalleClienteIvrDto>();
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        var laAbriYo = conn.State != System.Data.ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync();

        try
        {
            await using var cmd = new SqlCommand(sql.ToString(), conn) { CommandTimeout = 300 };
            cmd.Parameters.AddRange(parametros.ToArray());
            await using var rd = await cmd.ExecuteReaderAsync();

            string?   Texto(int i)  => rd.IsDBNull(i) ? null : rd.GetString(i);
            DateOnly? Fecha(int i)  => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));
            decimal?  Numero(int i) => rd.IsDBNull(i) ? null : rd.GetDecimal(i);

            while (await rd.ReadAsync())
            {
                var rango = rd.GetInt32(8);
                filas.Add(new DetalleClienteIvrDto
                {
                    Cedula             = rd.GetInt64(0),
                    Nombre             = Texto(1),
                    Lider              = Texto(2) ?? $"Líder {rd.GetInt32(19)}",
                    Cartera            = Texto(3) ?? $"Cartera {rd.GetInt32(20)}",
                    Telefonos          = Texto(4),
                    Marcaciones        = rd.GetInt32(5),
                    PrimeraMarcacion   = DateOnly.FromDateTime(rd.GetDateTime(6)),
                    UltimaMarcacion    = DateOnly.FromDateTime(rd.GetDateTime(7)),
                    RangoDesenlace     = rango,
                    MejorDesenlace     = NombreDesenlaceDetalle(rango),
                    UltimoResultado    = Texto(9),
                    UltimoPerfil       = Texto(10),
                    MejorPerfil        = Texto(11),
                    FechaUltimaGestion = Fecha(12),
                    FechaPromesa       = Fecha(13),
                    ValorPromesa       = Numero(14),
                    SaldoTotal         = Numero(15),
                    PagosPosteriores   = rd.GetInt32(16),
                    ValorPagado        = rd.GetDecimal(17),
                    FechaUltimoPago    = Fecha(18),
                });
            }
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }

        return filas;
    }

    /// <summary>
    /// Nombre del desenlace para el Excel. Los rangos 98 y 99 no son desenlaces del catálogo
    /// y se nombran acá con las mismas palabras que usa el análisis.
    /// </summary>
    private static string NombreDesenlaceDetalle(int rango) => rango switch
    {
        CatalogoVirtualidad.RangoSinResultado => "Sin resultado cargado",
        CatalogoVirtualidad.RangoSinContacto  => "Sin contacto (falla técnica o sin contestar)",
        _                                     => CatalogoVirtualidad.NombreDesenlace(rango),
    };

    private async Task<List<CeldaIvr>> ConsultarCeldasAsync(
        DateOnly desde, DateOnly hasta, int? liderID, int? carteraID)
    {
        // La deduplicación por cliente con jerarquía se resuelve en SQL: MIN(rango) por
        // (cartera, documento) es "su mejor desenlace". Agrupar después deja a lo sumo
        // (líderes × carteras × 6) filas, así que el informe completo sale de una sola consulta.
        var sql = new StringBuilder($"""
            ;WITH Mejor AS (
                SELECT h.LiderID, h.CarteraID, h.NumeroDocumento,
                       MIN({RangoSql}) AS Rango,
                       COUNT(*) AS Marcaciones
                  FROM dbo.HistoricoVirtualidad h
                 WHERE h.Canal = @canal
                   AND h.FechaEnvio >= @desde AND h.FechaEnvio <= @hasta
            """);

        if (liderID.HasValue)   sql.AppendLine("       AND h.LiderID = @liderID");
        if (carteraID.HasValue) sql.AppendLine("       AND h.CarteraID = @carteraID");

        sql.AppendLine("""
                 GROUP BY h.LiderID, h.CarteraID, h.NumeroDocumento
            )
            SELECT LiderID, CarteraID, Rango,
                   COUNT(*)          AS Clientes,
                   SUM(Marcaciones)  AS Marcaciones
              FROM Mejor
             GROUP BY LiderID, CarteraID, Rango;
            """);

        var parametros = new List<SqlParameter>
        {
            new("@canal", CANAL),
            new("@desde", desde.ToDateTime(TimeOnly.MinValue)),
            new("@hasta", hasta.ToDateTime(TimeOnly.MinValue)),
        };
        if (liderID.HasValue)   parametros.Add(new SqlParameter("@liderID", liderID.Value));
        if (carteraID.HasValue) parametros.Add(new SqlParameter("@carteraID", carteraID.Value));

        var celdas = new List<CeldaIvr>();
        var conn = (SqlConnection)_db.Database.GetDbConnection();
        var laAbriYo = conn.State != System.Data.ConnectionState.Open;
        if (laAbriYo) await _db.Database.OpenConnectionAsync();

        try
        {
            await using var cmd = new SqlCommand(sql.ToString(), conn) { CommandTimeout = 300 };
            cmd.Parameters.AddRange(parametros.ToArray());
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                celdas.Add(new CeldaIvr(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4)));
        }
        finally
        {
            if (laAbriYo) await _db.Database.CloseConnectionAsync();
        }

        return celdas;
    }

    // ── Derivaciones ────────────────────────────────────────────────────────

    private sealed record Metricas(
        int Marcados, int SinResultado, int ConResultado, int Contactados, int Menu, int Opcion1,
        decimal Contactabilidad, decimal ConversionOpcion1);

    private static Metricas Calcular(List<CeldaIvr> celdas)
    {
        var marcados     = celdas.Sum(c => c.Clientes);
        var sinResultado = celdas.Where(c => c.Rango == CatalogoVirtualidad.RangoSinResultado).Sum(c => c.Clientes);
        var conResultado = marcados - sinResultado;

        // Contactados = los 5 desenlaces reales. Quedan fuera tanto las fallas técnicas
        // (99) como los clientes sin resultado cargado (98).
        var contactados = celdas
            .Where(c => c.Rango != CatalogoVirtualidad.RangoSinContacto
                     && c.Rango != CatalogoVirtualidad.RangoSinResultado)
            .Sum(c => c.Clientes);
        var menu    = celdas.Where(c => CatalogoVirtualidad.RangosEscuchoMenu.Contains(c.Rango)).Sum(c => c.Clientes);
        var opcion1 = celdas.Where(c => c.Rango == 1).Sum(c => c.Clientes);

        return new Metricas(marcados, sinResultado, conResultado, contactados, menu, opcion1,
            Pct(contactados, conResultado), Pct(opcion1, contactados));
    }

    private static List<EmbudoPasoIvrDto> ConstruirEmbudo(int conResultado, int contactados, int menu, int opcion1) =>
    [
        new() { Etiqueta = "Clientes con resultado", Clientes = conResultado, Porcentaje = 100m,                        Base = "base" },
        new() { Etiqueta = "Contactados",            Clientes = contactados,  Porcentaje = Pct(contactados, conResultado), Base = "de marcados" },
        new() { Etiqueta = "Escuchó el menú",        Clientes = menu,         Porcentaje = Pct(menu, conResultado),        Base = "de marcados" },
        new() { Etiqueta = "Marcaron opción 1",      Clientes = opcion1,      Porcentaje = Pct(opcion1, contactados),      Base = "de contactó" },
    ];

    private static List<DesenlaceIvrDto> ConstruirDesenlaces(List<CeldaIvr> celdas, int contactados) =>
        CatalogoVirtualidad.DesenlacesIvr
            .Select(d =>
            {
                var clientes = celdas.Where(c => c.Rango == d.Rango).Sum(c => c.Clientes);
                return new DesenlaceIvrDto
                {
                    Rango      = d.Rango,
                    Desenlace  = d.Desenlace,
                    Clientes   = clientes,
                    Porcentaje = Pct(clientes, contactados),
                };
            })
            .ToList();

    private static decimal Pct(int parte, int total) =>
        total > 0 ? Math.Round(100m * parte / total, 2) : 0m;

    // ── Apreciaciones en prosa ──────────────────────────────────────────────

    private static string ApreciacionGlobal(AnalisisIvrDto d)
    {
        if (d.ClientesMarcados == 0) return "Sin datos en el período.";

        // Sin resultados cargados no hay nada que analizar: decirlo así evita que se lea
        // como "contactabilidad del 0 %", que sería una conclusión falsa.
        if (d.ClientesConResultado == 0)
            return $"Los {d.ClientesMarcados:N0} clientes marcados en el período no tienen resultado cargado, " +
                   "así que todavía no se puede medir contactabilidad. Corresponden al flujo anterior de dos " +
                   "fases, que registraba el envío pero nunca recibió su resultado.";

        var sb = new StringBuilder();

        if (d.SinResultado > 0)
            sb.Append($"{d.SinResultado:N0} de {d.ClientesMarcados:N0} clientes no tienen resultado cargado y " +
                      "quedan fuera del cálculo. ");

        sb.Append($"De {d.ClientesConResultado:N0} clientes con resultado, {d.Contactados:N0} contestaron ")
          .Append($"({d.Contactabilidad:N1}%). ");

        if (d.Contactabilidad < 30)
            sb.Append($"La contactabilidad es baja: cerca de {Math.Round((100 - d.Contactabilidad) / 100 * 5):N0} de cada 5 marcaciones no logran contacto. ");

        sb.Append($"De quienes contestaron, {d.ConversionOpcion1:N2}% marcó la opción 1 ")
          .Append($"({d.MarcaronOpcion1:N0} transferencias). ");

        if (d.Contactabilidad < 30 && d.ConversionOpcion1 < 5)
            sb.Append("El punto crítico no está en la atención sino antes: en llegar al cliente y en el mensaje que escucha.");

        return sb.ToString().Trim();
    }

    private static string ApreciacionLider(AnalisisIvrLiderDto l, AnalisisIvrDto global)
    {
        if (l.ClientesMarcados == 0) return "Sin marcaciones en el período.";
        if (l.ClientesConResultado == 0)
            return $"Sus {l.ClientesMarcados:N0} clientes marcados no tienen resultado cargado, " +
                   "así que no se puede medir su contactabilidad en este período.";

        var sb = new StringBuilder();

        var difCont = l.Contactabilidad - global.Contactabilidad;
        sb.Append($"Su contactabilidad del {l.Contactabilidad:N1}% ")
          .Append(Math.Abs(difCont) < 1m
              ? $"está en línea con el promedio general del {global.Contactabilidad:N1}%. "
              : difCont > 0
                  ? $"supera el promedio general del {global.Contactabilidad:N1}%. "
                  : $"queda por debajo del promedio general del {global.Contactabilidad:N1}%, lo que indica que buena parte de la base marcada no está contestando. ");

        if (l.Contactados > 0)
        {
            var difConv = l.ConversionOpcion1 - global.ConversionOpcion1;
            sb.Append($"La conversión a opción 1 del {l.ConversionOpcion1:N2}% ")
              .Append(Math.Abs(difConv) < 0.1m
                  ? $"iguala el {global.ConversionOpcion1:N2}% general."
                  : difConv > 0
                      ? $"está por encima del {global.ConversionOpcion1:N2}% general, señal de que el mensaje está funcionando mejor que el promedio."
                      : $"está por debajo del {global.ConversionOpcion1:N2}% general.");
        }
        else
        {
            sb.Append("No registra contactos efectivos en el período.");
        }

        return sb.ToString().Trim();
    }
}

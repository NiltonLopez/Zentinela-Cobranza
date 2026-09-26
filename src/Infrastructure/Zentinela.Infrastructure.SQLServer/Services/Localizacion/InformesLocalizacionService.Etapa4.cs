using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Etapa 4 de los Informes de Localización (ADR-040): tendencia semanal, ranking de asesores y "Alertas de la
/// semana". Todo sale de las tablas del precálculo (cortes guardados 26 semanas) y de los lotes de envío; el
/// alcance ya viene resuelto por el controlador (para el Líder, su liderID y carteras suyas).
/// </summary>
internal sealed partial class InformesLocalizacionService
{
    // ════════════════════════════════════════════════════════════════════════
    //  Tendencia semanal
    // ════════════════════════════════════════════════════════════════════════

    public async Task<TendenciaLocalizacionDto> ObtenerTendenciaAsync(
        int? liderID, int? carteraID, int semanas, CancellationToken ct = default)
    {
        var cfg = _opciones.Tendencia;
        semanas = Math.Clamp(semanas <= 0 ? cfg.SemanasPorDefecto : semanas, 1, Math.Max(1, cfg.SemanasMaximas));

        var s = await ObtenerSnapshotAsync(ct);
        var catalogo = await CatalogoAsync(ct);
        var alcance = ResolverAlcance(s, liderID, carteraID);
        var hoy = _reloj.Ahora;

        // Los cortes viejos no cambian: la clave solo cambia con un corte nuevo (Version) o un día nuevo.
        var clave = $"localizacion:tendencia:{_p}:{_coordinador.Version}:{liderID}:{carteraID}:{semanas}:{hoy:yyyyMMdd}";
        if (_cache.TryGetValue(clave, out TendenciaLocalizacionDto? enCache) && enCache is not null) return enCache;

        await using var cx = await ConexionAsync(ct);
        var cortes = await LeerCortesOkAsync(cx.Conn, TendenciaLocalizacion.Desde(hoy, semanas), null, ct);
        var semanales = TendenciaLocalizacion.CortesSemanales(cortes, hoy, semanas);

        var metricas = new Dictionary<int, MetricasLocalizacion>();
        var porFuente = new List<(int CorteID, string Fuente, int Total, int Buscados, int Encontrados)>();
        if (semanales.Count > 0)
        {
            await using var cmd = new SqlCommand(SqlTendencia(alcance.Tipo), cx.Conn) { CommandTimeout = 120 };
            cmd.Parameters.Add("@Cortes", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(semanales.Select(c => c.CorteID));
            cmd.Parameters.Add("@LiderID", SqlDbType.Int).Value = (object?)liderID ?? 0;
            cmd.Parameters.Add("@CarteraID", SqlDbType.Int).Value = (object?)carteraID ?? 0;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) metricas[rd.GetInt32(0)] = LeerMetricas(rd, 1);
            await rd.NextResultAsync(ct);
            while (await rd.ReadAsync(ct))
                porFuente.Add((rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetInt32(4)));
        }

        var dto = new TendenciaLocalizacionDto
        {
            Alcance = alcance.Tipo,
            EtiquetaAlcance = alcance.Etiqueta,
            SemanasPedidas = semanas,
            Pesos = new PesosIndiceDto { Contacto = _opciones.PesoContacto, Cobertura = _opciones.PesoCobertura, Frescura = _opciones.PesoFrescura },
            Semanas = semanales
                .Where(c => metricas.ContainsKey(c.CorteID))
                .Select(c => new SemanaTendenciaDto
                {
                    InicioSemana = c.InicioSemana, CorteID = c.CorteID, FechaCorte = c.FechaCorte,
                    Metricas = MapearMetricas(metricas[c.CorteID]),
                })
                .ToList(),
        };

        var semanaDe = semanales.ToDictionary(c => c.CorteID, c => c.InicioSemana);
        dto.Fuentes = catalogo
            .Select(f => new FuenteTendenciaDto
            {
                Clave = f.Clave, Etiqueta = f.Etiqueta, Estado = f.Estado,
                Puntos = porFuente
                    .Where(r => r.Fuente.Equals(f.Clave, StringComparison.OrdinalIgnoreCase) && semanaDe.ContainsKey(r.CorteID))
                    .OrderBy(r => semanaDe[r.CorteID])
                    .Select(r => new PuntoFuenteTendenciaDto
                    {
                        InicioSemana = semanaDe[r.CorteID], Total = r.Total, Buscados = r.Buscados, Encontrados = r.Encontrados,
                        PctCobertura = CalculoLocalizacion.Pct(r.Buscados, r.Total),
                        PctLocalizacion = CalculoLocalizacion.Pct(r.Encontrados, r.Total),
                    })
                    .ToList(),
            })
            .Where(f => f.Puntos.Count > 0)
            .ToList();

        _cache.Set(clave, dto, TimeSpan.FromMinutes(Math.Max(1, _opciones.MinutosCacheLectura)));
        return dto;
    }

    /// <summary>
    /// Métricas y filas por fuente de los cortes <c>@Cortes</c> (JSON de CorteIDs) para el universo del Resumen: el par,
    /// la suma de los pares de la cartera, el líder (titulares distintos, todas sus relaciones) o toda la base.
    /// </summary>
    private string SqlTendencia(string tipoAlcance)
    {
        const string Ids = "CorteID IN (SELECT CorteID FROM @Ids)";
        var sumas = string.Join(", ", ColumnasMetricas.Split(',').Select(c => $"SUM({c.Trim()})"));
        var (metricas, fuentes) = tipoAlcance switch
        {
            "par" => (
                $"SELECT CorteID, {ColumnasMetricas} FROM {_p}LocalizacionResumenPar WHERE {Ids} AND LiderID = @LiderID AND CarteraID = @CarteraID;",
                $"SELECT CorteID, Fuente, Total, Buscados, Encontrados FROM {_p}LocalizacionResumenFuente WHERE {Ids} AND LiderID = @LiderID AND CarteraID = @CarteraID;"),
            "cartera" => (
                $"SELECT CorteID, {sumas} FROM {_p}LocalizacionResumenPar WHERE {Ids} AND CarteraID = @CarteraID GROUP BY CorteID;",
                $"SELECT CorteID, Fuente, SUM(Total), SUM(Buscados), SUM(Encontrados) FROM {_p}LocalizacionResumenFuente WHERE {Ids} AND CarteraID = @CarteraID GROUP BY CorteID, Fuente;"),
            // líder, o toda la base (LiderID 0): titulares distintos, todas las relaciones (como el Resumen).
            _ => (
                $"SELECT CorteID, {ColumnasMetricas} FROM {_p}LocalizacionResumenLider WHERE {Ids} AND LiderID = @LiderID AND SoloInternas = 0;",
                $"SELECT CorteID, Fuente, Total, Buscados, Encontrados FROM {_p}LocalizacionResumenFuenteLider WHERE {Ids} AND LiderID = @LiderID;"),
        };
        return $"""
            SET NOCOUNT ON;
            DECLARE @Ids TABLE (CorteID int NOT NULL PRIMARY KEY);
            INSERT INTO @Ids (CorteID) SELECT DISTINCT CAST(value AS int) FROM OPENJSON(@Cortes);
            {metricas}
            {fuentes}
            """;
    }

    /// <summary>Cortes OK con <c>FechaCorte</c> en [desde, hasta).</summary>
    private async Task<List<(int CorteID, DateTime FechaCorte)>> LeerCortesOkAsync(
        SqlConnection conn, DateTime desde, DateTime? hasta, CancellationToken ct)
    {
        var lista = new List<(int, DateTime)>();
        await using var cmd = new SqlCommand($"""
            SELECT CorteID, FechaCorte FROM {_p}LocalizacionCorte
             WHERE Estado = 'OK' AND FechaCorte >= @Desde AND (@Hasta IS NULL OR FechaCorte < @Hasta);
            """, conn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde;
        cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = (object?)hasta ?? DBNull.Value;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) lista.Add((rd.GetInt32(0), rd.GetDateTime(1)));
        return lista;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Ranking de asesores
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Una fila de <c>LocalizacionResumenAsesor</c> (par × asesor) con si el asesor sigue activo.</summary>
    private sealed record FilaAsesorPar(int LiderID, int CarteraID, long AgenteID, bool Activo, MetricasAsesor Metricas);

    private sealed class SnapshotAsesores
    {
        public bool TablaExiste { get; init; }
        public List<FilaAsesorPar> Actual { get; init; } = [];
        public List<FilaAsesorPar> Anterior { get; init; } = [];
        public Dictionary<long, string> Nombres { get; init; } = [];
    }

    public async Task<AsesoresLocalizacionDto> ObtenerAsesoresAsync(
        int? liderID, int? carteraID, CriterioRankingAsesor criterio, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var alcance = ResolverAlcance(s, liderID, carteraID);
        var cfg = _opciones.Asesores;
        var min = Math.Max(1, cfg.MinTitulares);

        var dto = new AsesoresLocalizacionDto
        {
            Corte = s.Corte,
            CorteAnterior = s.CorteAnterior,
            Alcance = alcance.Tipo,
            EtiquetaAlcance = alcance.Etiqueta,
            Criterio = criterio.ToString(),
            MenorEsMejor = RankingAsesoresCalculo.MenorEsMejor(criterio),
            MinTitulares = min,
            DiasGestionReciente = ParametrosAsesores.DiasGestion(cfg),
        };

        var snap = await ObtenerSnapshotAsesoresAsync(s, ct);
        if (!snap.TablaExiste)
        {
            dto.Aviso = $"Falta la tabla LocalizacionResumenAsesor: aplica la versión actual de {LocalizacionTablasFaltantesException.Script} "
                      + "(es idempotente) y el ranking aparece con el siguiente corte.";
            return dto;
        }

        bool EnAlcance(FilaAsesorPar r) =>
            (liderID is null || r.LiderID == liderID) && (carteraID is null || r.CarteraID == carteraID);

        var actuales = snap.Actual.Where(EnAlcance).ToList();
        dto.TitularesAlcance = s.Pares
            .Where(p => (liderID is null || p.LiderID == liderID) && (carteraID is null || p.CarteraID == carteraID))
            .Sum(p => p.Metricas.Total);
        dto.TitularesSinAsesor = Math.Max(0, dto.TitularesAlcance - actuales.Sum(r => r.Metricas.Titulares));

        var asesores = actuales
            .Where(r => r.Activo)
            .GroupBy(r => r.AgenteID)
            .Select(g => (Documento: g.Key,
                          Nombre: snap.Nombres.GetValueOrDefault(g.Key) ?? $"Asesor {g.Key}",
                          M: MetricasAsesor.Sumar(g.Select(r => r.Metricas)),
                          Carteras: g.Select(r => r.CarteraID).Distinct().Count()))
            .ToList();
        var anteriores = snap.Anterior
            .Where(r => r.Activo && EnAlcance(r))
            .GroupBy(r => r.AgenteID)
            .ToDictionary(g => g.Key, g => MetricasAsesor.Sumar(g.Select(r => r.Metricas)));

        var (ranking, pocos) = RankingAsesoresCalculo.Rankear(asesores, a => a.M, a => a.Nombre, criterio, min);

        FilaAsesorDto Fila(int? posicion, (long Documento, string Nombre, MetricasAsesor M, int Carteras) a, decimal? valor)
        {
            var m = a.M;
            decimal? anterior = anteriores.TryGetValue(a.Documento, out var ma) && ma.Titulares >= min
                ? RankingAsesoresCalculo.Valor(ma, criterio) : null;
            return new FilaAsesorDto
            {
                Posicion                = posicion,
                Documento               = a.Documento,
                Nombre                  = a.Nombre,
                Valor                   = valor,
                Delta                   = posicion is null ? null : Delta(valor, anterior),
                Titulares               = m.Titulares,
                ContactoEfectivo        = m.ContactoEfectivo,
                PctContactoEfectivo     = CalculoLocalizacion.Pct(m.ContactoEfectivo, m.Titulares),
                GestionadosRecientes    = m.GestionadosRecientes,
                PctGestionadosRecientes = CalculoLocalizacion.Pct(m.GestionadosRecientes, m.Titulares),
                ConDato                 = m.ConDato,
                PctConDato              = CalculoLocalizacion.Pct(m.ConDato, m.Titulares),
                ConSaldo                = m.ConSaldo,
                SinContactoConSaldo     = m.SinContactoConSaldo,
                PctSinContactoConSaldo  = RankingAsesoresCalculo.PctSinContactoConSaldo(m),
                SaldoSinContacto        = m.SaldoSinContacto,
                Carteras                = a.Carteras,
            };
        }

        dto.Asesores = ranking.Select(f => Fila(f.Posicion, f.Item, f.Valor)).ToList();
        dto.PocosTitulares = pocos.Select(a => Fila(null, a, RankingAsesoresCalculo.Valor(a.M, criterio))).ToList();
        if (dto.Asesores.Count == 0 && dto.PocosTitulares.Count == 0)
            dto.Aviso = "Ningún asesor activo hizo la última gestión de los titulares de este alcance.";
        return dto;
    }

    /// <summary>
    /// Las filas por par y asesor del corte y del anterior (~4.000 cada uno) y los nombres de RRHH, una vez por corte
    /// (clave con la versión del coordinador, como el snapshot).
    /// </summary>
    private async Task<SnapshotAsesores> ObtenerSnapshotAsesoresAsync(Snapshot s, CancellationToken ct)
    {
        var clave = $"localizacion:asesores:{_p}:{_coordinador.Version}:{s.Corte.CorteID}";
        if (_cache.TryGetValue(clave, out SnapshotAsesores? enCache) && enCache is not null) return enCache;

        var actual = new List<FilaAsesorPar>();
        var anterior = new List<FilaAsesorPar>();
        await using (var cx = await ConexionAsync(ct))
        {
            await using var cmd = new SqlCommand($"""
                SELECT r.CorteID, r.LiderID, r.CarteraID, r.AgenteID,
                       CAST(CASE WHEN a.Activo = 1 THEN 1 ELSE 0 END AS bit) AS Activo,
                       r.Titulares, r.ConDato, r.ConSaldo, r.SinContactoConSaldo, r.SaldoSinContacto,
                       r.GestionadosRecientes, r.ContactoEfectivo
                  FROM {_p}LocalizacionResumenAsesor r
                  LEFT JOIN dbo.Agente a ON a.AgenteID = r.AgenteID
                 WHERE r.CorteID IN (@Corte, @Anterior);
                """, cx.Conn) { CommandTimeout = 120 };
            cmd.Parameters.Add("@Corte", SqlDbType.Int).Value = s.Corte.CorteID;
            cmd.Parameters.Add("@Anterior", SqlDbType.Int).Value = (object?)s.CorteAnterior?.CorteID ?? DBNull.Value;
            try
            {
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var fila = new FilaAsesorPar(rd.GetInt32(1), rd.GetInt32(2), rd.GetInt64(3), rd.GetBoolean(4),
                        new MetricasAsesor(rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7), rd.GetInt32(8), rd.GetDecimal(9),
                                           rd.GetInt32(10), rd.GetInt32(11)));
                    (rd.GetInt32(0) == s.Corte.CorteID ? actual : anterior).Add(fila);
                }
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                // Script aplicado antes de la etapa 4: el resto del módulo funciona; el ranking de asesores avisa.
                return new SnapshotAsesores { TablaExiste = false };
            }
        }

        // Nombres: RRHH por cédula (Agente no guarda el nombre). Si RRHH no responde, "Asesor <cédula>".
        var documentos = actual.Concat(anterior).Where(r => r.Activo).Select(r => r.AgenteID).Distinct().ToList();
        var nombres = new Dictionary<long, string>();
        try
        {
            var empleados = await _rrhh.Empleados.AsNoTracking()
                .Where(e => documentos.Contains(e.NumeroDocumento))
                .Select(e => new { e.NumeroDocumento, e.Nombre, e.Apellidos })
                .ToListAsync(ct);
            foreach (var e in empleados)
                nombres[e.NumeroDocumento] = $"{e.Nombre} {e.Apellidos}".Trim();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // Sin nombres no se cae el ranking.
        }

        var snap = new SnapshotAsesores { TablaExiste = true, Actual = actual, Anterior = anterior, Nombres = nombres };
        _cache.Set(clave, snap, TimeSpan.FromMinutes(Math.Max(1, _opciones.MinutosCacheLectura)));
        return snap;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Alertas de la semana
    // ════════════════════════════════════════════════════════════════════════

    public async Task<AlertasSemanaDto> ObtenerAlertasSemanaAsync(int? liderID, int? carteraID, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var catalogo = await CatalogoAsync(ct);
        var u = _opciones.AlertasSemana;
        var hoy = _reloj.Ahora.Date;
        var semana = TendenciaLocalizacion.InicioSemana(hoy);

        var dto = new AlertasSemanaDto
        {
            Semana = semana,
            Corte = s.Corte,
            Umbrales = new UmbralesAlertasSemanaDto
            {
                CaidaIndicePuntos = u.CaidaIndicePuntos, DiasLoteSinRetorno = u.DiasLoteSinRetorno,
                DiasFuenteSinCargue = u.DiasFuenteSinCargue, SinContactoConSaldoPct = u.SinContactoConSaldoPct,
                ColaGrande = u.ColaGrande, DiasSinLotes = u.DiasSinLotes,
            },
        };
        var alertas = new List<AlertaSemanaDto>();

        // Universo: los pares INTERNOS del alcance (el de los rankings por defecto: sin externas ni BASE GENERAL).
        bool ParEnAlcance(int l, int c) => (liderID is null || l == liderID) && (carteraID is null || c == carteraID);
        var pares = s.Pares.Where(p => p.Interna && ParEnAlcance(p.LiderID, p.CarteraID)).ToList();
        var lideres = pares.Select(p => p.LiderID).ToHashSet();

        await using var cx = await ConexionAsync(ct);

        // 1. Caída del índice por líder contra el último corte de la semana anterior.
        var cortes = await LeerCortesOkAsync(cx.Conn, semana.AddDays(-7), semana, ct);
        var previo = TendenciaLocalizacion.SemanaAnterior(cortes, hoy);
        if (previo is null)
        {
            dto.Avisos.Add("Todavía no hay un corte de la semana anterior: la caída del índice se evalúa cuando lo haya.");
        }
        else
        {
            var (cortePrevio, lideresPrevios) = await LeerLideresCorteAsync(cx.Conn, previo.CorteID, ct);
            dto.CorteSemanaAnterior = cortePrevio;
            alertas.AddRange(AlertasSemanaLocalizacion.CaidasDeIndice(
                s.Lideres
                    .Where(l => l.SoloInternas && l.LiderID != 0 && lideres.Contains(l.LiderID))
                    .Select(l => new IndiceSemanalLider(
                        l.LiderID, s.NombreLider(l.LiderID), s.DocumentoLider(l.LiderID),
                        CalculoLocalizacion.Indice(l.Metricas, _opciones),
                        lideresPrevios.TryGetValue(l.LiderID, out var mp) ? CalculoLocalizacion.Indice(mp, _opciones) : null)),
                u));
        }

        // 2 y 5. Lotes (tolerante a que falten las tablas de envíos).
        List<(int EnvioID, string Codigo, int LiderID, int CarteraID, string Fuente, string Estado, DateTime FechaEnvio, long? Cargue, int Titulares)>? lotes = null;
        try
        {
            lotes = await LeerLotesEnviadosAsync(cx.Conn, hoy.AddDays(-Math.Max(1, u.DiasSinLotes)), ct);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            dto.Avisos.Add($"Faltan las tablas de envíos ({LocalizacionTablasFaltantesException.Script}): no se revisan lotes sin retorno ni líderes sin lotes.");
        }

        if (lotes is not null)
        {
            alertas.AddRange(AlertasSemanaLocalizacion.LotesSinRetorno(
                lotes.Where(l => l.Estado == "ENVIADO" && ParEnAlcance(l.LiderID, l.CarteraID))
                     .Select(l => new LoteEnviadoAlerta(l.EnvioID, l.Codigo, l.LiderID, s.NombreLider(l.LiderID), s.DocumentoLider(l.LiderID),
                                                        l.CarteraID, s.NombreCartera(l.CarteraID), l.Fuente, l.FechaEnvio, l.Cargue, l.Titulares)),
                hoy, u));

            // Cola aproximada de cada líder del alcance (sus pares internos, fuentes Funcionales) y sus lotes recientes.
            var funcionales = catalogo.Where(f => f.Estado == EstadoFuente.Funcional).Select(f => f.Clave)
                                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var paresInternos = pares.Select(p => (p.LiderID, p.CarteraID)).ToHashSet();
            var desdeLotes = hoy.AddDays(-Math.Max(1, u.DiasSinLotes));
            alertas.AddRange(AlertasSemanaLocalizacion.ColasSinLotes(
                lideres.Select(l => new ColaLiderAlerta(
                    l, s.NombreLider(l), s.DocumentoLider(l),
                    UsoDepartamentoCalculo.Cola(s.Fuentes
                        .Where(r => r.LiderID == l && paresInternos.Contains((r.LiderID, r.CarteraID)) && funcionales.Contains(r.Fuente))
                        .Select(r => (r.Total, r.Buscados, r.Encontrados, r.Viejos180))),
                    pares.Where(p => p.LiderID == l).Sum(p => p.Metricas.Total - p.Metricas.BuscadosFuncional),
                    lotes.Count(x => x.LiderID == l && x.FechaEnvio >= desdeLotes))),
                u));
        }

        // 3. Fuentes Funcionales sin datos nuevos: último cargue OK del módulo o dato más reciente de la fuente en toda la
        //    base (las fuentes que aún se cargan con los SP legacy no dejan CargueFuenteEjecucion). Se muestra en cualquier alcance.
        alertas.AddRange(AlertasSemanaLocalizacion.FuentesSinCargue(
            catalogo.Select(f => new FuenteCargueAlerta(f.Clave, f.Etiqueta, f.Estado, f.UltimoCargueOk,
                s.FuentesLider.FirstOrDefault(r => r.LiderID == 0 && r.Fuente.Equals(f.Clave, StringComparison.OrdinalIgnoreCase))?.UltimaConsultaMax)),
            hoy, u));

        // 4. Carteras con mucho "sin contacto con saldo": la cartera (suma de sus pares internos del alcance) o, con un
        //    líder elegido, su parte de cada cartera.
        var carteras = liderID is { } lid
            ? pares.Select(p => new CarteraAlerta(p.CarteraID, s.NombreCartera(p.CarteraID), lid, s.NombreLider(lid), p.Metricas))
            : pares.GroupBy(p => p.CarteraID)
                   .Select(g => new CarteraAlerta(g.Key, s.NombreCartera(g.Key), null, null, MetricasLocalizacion.Sumar(g.Select(p => p.Metricas))));
        alertas.AddRange(AlertasSemanaLocalizacion.CarterasSinContacto(carteras, u));

        dto.Alertas = AlertasSemanaLocalizacion.Ordenar(alertas);
        return dto;
    }

    /// <summary>El corte y sus métricas por líder (SoloInternas = 1, el universo de los rankings).</summary>
    private async Task<(CorteLocalizacionDto? Corte, Dictionary<int, MetricasLocalizacion> Lideres)> LeerLideresCorteAsync(
        SqlConnection conn, int corteID, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"""
            SELECT {ColumnasCorte} FROM {_p}LocalizacionCorte WHERE CorteID = @CorteID;
            SELECT LiderID, {ColumnasMetricas} FROM {_p}LocalizacionResumenLider
             WHERE CorteID = @CorteID AND SoloInternas = 1 AND LiderID <> 0;
            """, conn);
        cmd.Parameters.Add("@CorteID", SqlDbType.Int).Value = corteID;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var corte = await rd.ReadAsync(ct) ? LeerCorte(rd) : null;
        await rd.NextResultAsync(ct);
        var lideres = new Dictionary<int, MetricasLocalizacion>();
        while (await rd.ReadAsync(ct)) lideres[rd.GetInt32(0)] = LeerMetricas(rd, 1);
        return (corte, lideres);
    }

    /// <summary>Lotes ENVIADOS (todos) y CERRADOS enviados desde <paramref name="desde"/> (para "sin lotes recientes").</summary>
    private async Task<List<(int EnvioID, string Codigo, int LiderID, int CarteraID, string Fuente, string Estado, DateTime FechaEnvio, long? Cargue, int Titulares)>>
        LeerLotesEnviadosAsync(SqlConnection conn, DateTime desde, CancellationToken ct)
    {
        var lista = new List<(int, string, int, int, string, string, DateTime, long?, int)>();
        await using var cmd = new SqlCommand($"""
            SELECT EnvioID, Codigo, LiderID, CarteraID, Fuente, Estado, FechaEnvio, CargueEjecucionID, Titulares
              FROM {_p}LocalizacionEnvio
             WHERE FechaEnvio IS NOT NULL
               AND (Estado = 'ENVIADO' OR (Estado = 'CERRADO' AND FechaEnvio >= @Desde));
            """, conn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            lista.Add((rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), rd.GetInt32(3), rd.GetString(4), rd.GetString(5),
                       rd.GetDateTime(6), rd.IsDBNull(7) ? null : rd.GetInt64(7), rd.GetInt32(8)));
        return lista;
    }
}

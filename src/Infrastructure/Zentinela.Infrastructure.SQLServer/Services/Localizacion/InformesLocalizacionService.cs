using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Lecturas de los Informes de Localización (ADR-040). Todo sale del último corte OK del precálculo:
/// el corte completo (~2.000 filas) se carga una vez en memoria (<see cref="Snapshot"/>) y cada endpoint
/// lo filtra y agrega en C#, así Resumen, Cobertura, Frescura y Rankings responden en milisegundos.
///
/// Caché: el snapshot vive <see cref="InformesLocalizacionSettings.MinutosCacheLectura"/> minutos y su
/// clave lleva <see cref="ILocalizacionResumenCoordinador.Version"/>: un corte nuevo lo invalida al instante.
///
/// Universos:
/// <list type="bullet">
/// <item>par (líder + cartera): filas de <c>LocalizacionResumenFuente/Par</c>.</item>
/// <item>cartera (solo Admin/Director): suma de sus pares activos = filas de Titular_Cartera, como la
///   "Cartera Completo" del Calificador (que además cuenta relaciones inactivas).</item>
/// <item>líder: titulares distintos (<c>LocalizacionResumenFuenteLider/Lider</c>).</item>
/// <item>global (sin filtros): titulares distintos de toda la base (LiderID 0).</item>
/// </list>
/// </summary>
internal sealed class InformesLocalizacionService : IInformesLocalizacionService
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    private readonly CallCenterDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILocalizacionResumenCoordinador _coordinador;
    private readonly IFuentesLocalizacionService _fuentes;
    private readonly InformesLocalizacionSettings _opciones;

    public InformesLocalizacionService(
        CallCenterDbContext db,
        IMemoryCache cache,
        ILocalizacionResumenCoordinador coordinador,
        IFuentesLocalizacionService fuentes,
        IOptions<InformesLocalizacionSettings> opciones)
    {
        _db = db;
        _cache = cache;
        _coordinador = coordinador;
        _fuentes = fuentes;
        _opciones = opciones.Value;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Estado
    // ════════════════════════════════════════════════════════════════════════

    public async Task<EstadoLocalizacionDto> ObtenerEstadoAsync(CancellationToken ct = default)
    {
        await using var conn = await AbrirAsync(ct);
        await using var cmd = new SqlCommand($"""
            SELECT TOP (1) {ColumnasCorte} FROM dbo.LocalizacionCorte WHERE Estado = 'OK' ORDER BY CorteID DESC;
            SELECT TOP (1) {ColumnasCorte} FROM dbo.LocalizacionCorte ORDER BY CorteID DESC;
            """, conn);

        CorteLocalizacionDto? ok = null, ultimo = null;
        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct)) ok = LeerCorte(rd);
            await rd.NextResultAsync(ct);
            if (await rd.ReadAsync(ct)) ultimo = LeerCorte(rd);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new LocalizacionTablasFaltantesException();
        }

        _coordinador.VerificarVigencia(ok?.FechaCorte);

        return new EstadoLocalizacionDto
        {
            Calculando    = _coordinador.Calculando,
            MotivoEnCurso = _coordinador.MotivoEnCurso,
            InicioEnCurso = _coordinador.InicioEnCurso,
            UltimoOk      = ok,
            UltimoIntento = ultimo,
            Vigente       = ok is not null && !ReglaCorteLocalizacion.DebeRecalcular(ok.FechaCorte, DateTime.Now, alArrancar: false),
        };
    }

    public async Task<bool> EsCarteraDelLiderAsync(int liderID, int carteraID, CancellationToken ct = default) =>
        await _db.LiderCarteras.AsNoTracking()
            .AnyAsync(lc => lc.LiderID == liderID && lc.CarteraID == carteraID && lc.Activo, ct);

    // ════════════════════════════════════════════════════════════════════════
    //  Resumen
    // ════════════════════════════════════════════════════════════════════════

    public async Task<ResumenLocalizacionDto> ObtenerResumenAsync(int? liderID, int? carteraID, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var catalogo = await CatalogoAsync(ct);
        var alcance = ResolverAlcance(s, liderID, carteraID);
        var fuentes = FuentesDelAlcance(s, alcance, catalogo);

        return new ResumenLocalizacionDto
        {
            Corte           = s.Corte,
            Alcance         = alcance.Tipo,
            EtiquetaAlcance = alcance.Etiqueta,
            Metricas        = MapearMetricas(alcance.Metricas),
            Fuentes         = fuentes,
            Alertas         = Alertas(s, alcance, fuentes),
            Pesos           = new PesosIndiceDto
            {
                Contacto  = _opciones.PesoContacto,
                Cobertura = _opciones.PesoCobertura,
                Frescura  = _opciones.PesoFrescura,
            },
        };
    }

    private List<AlertaLocalizacionDto> Alertas(Snapshot s, Alcance a, List<FuenteResumenDto> fuentes)
    {
        var alertas = new List<AlertaLocalizacionDto>();
        var hoy = DateTime.Today;

        foreach (var f in fuentes.Where(f => f.Estado == EstadoFuente.Funcional))
        {
            if (f.PctFaltaPorBuscar is { } pct && pct > _opciones.AlertaPorBuscarFuncionalPct)
                alertas.Add(new AlertaLocalizacionDto
                {
                    Tipo = "POR_BUSCAR", Severidad = "alta", Fuente = f.Clave,
                    Mensaje = $"{f.Etiqueta} (Funcional): {pct.ToString("0.#", Co)} % de los titulares nunca se ha buscado "
                            + $"({f.FaltaPorBuscar.ToString("N0", Co)}).",
                });
        }

        foreach (var f in fuentes.Where(f => f.UltimaConsultaMax is not null))
        {
            var dias = (hoy - f.UltimaConsultaMax!.Value.Date).Days;
            if (dias > _opciones.AlertaDiasSinDato)
                alertas.Add(new AlertaLocalizacionDto
                {
                    Tipo = "DATO_VIEJO", Severidad = "media", Fuente = f.Clave,
                    Mensaje = $"{f.Etiqueta}: el dato más reciente es del {f.UltimaConsultaMax:dd/MM/yyyy} ({dias.ToString("N0", Co)} días).",
                });
        }

        foreach (var p in a.Pares)
        {
            var pct = CalculoLocalizacion.PctSinContactoConSaldo(p.Metricas);
            if (pct is { } v && v > _opciones.AlertaSinContactoConSaldoPct)
                alertas.Add(new AlertaLocalizacionDto
                {
                    Tipo = "SIN_CONTACTO_SALDO", Severidad = "alta", LiderID = p.LiderID, CarteraID = p.CarteraID,
                    Mensaje = $"{s.NombreCartera(p.CarteraID)} — {s.NombreLider(p.LiderID)}: {v.ToString("0.#", Co)} % de sus titulares "
                            + $"tiene saldo y no tiene contacto ({p.Metricas.SinContactoConSaldo.ToString("N0", Co)}, "
                            + $"{p.Metricas.SaldoSinContacto.ToString("C0", Co)}).",
                });
        }

        return alertas.OrderBy(x => x.Severidad == "alta" ? 0 : 1).ToList();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Cobertura
    // ════════════════════════════════════════════════════════════════════════

    public async Task<CoberturaLocalizacionDto> ObtenerCoberturaAsync(
        int? liderID, int? carteraID, string agrupar, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var catalogo = await CatalogoAsync(ct);
        agrupar = (agrupar ?? "par").Trim().ToLowerInvariant() switch
        {
            "cartera" => "cartera",
            "lider"   => "lider",
            _         => "par",
        };

        var pares = s.Fuentes
            .Where(r => (liderID is null || r.LiderID == liderID) && (carteraID is null || r.CarteraID == carteraID))
            .ToList();

        var filas = new List<FilaCoberturaDto>();

        if (agrupar == "par" || (agrupar == "lider" && carteraID is not null))
        {
            // Par, o "comparar a los líderes de una misma cartera": filas de cada par.
            foreach (var r in pares)
                filas.Add(FilaCobertura(
                    $"{r.LiderID}-{r.CarteraID}",
                    agrupar == "lider" ? s.NombreLider(r.LiderID) : $"{s.NombreCartera(r.CarteraID)} — {s.NombreLider(r.LiderID)}",
                    r.LiderID, r.CarteraID, esPar: true, s, catalogo, r));
        }
        else if (agrupar == "cartera")
        {
            foreach (var g in pares.GroupBy(r => (r.CarteraID, r.Fuente)))
            {
                var suma = FilaFuente.Sumar(g);
                // Si el grupo es un solo par (p. ej. el Líder mirando sus carteras) conserva la descarga.
                var unico = g.Select(x => x.LiderID).Distinct().Count() == 1 ? g.First().LiderID : (int?)null;
                filas.Add(FilaCobertura($"c{g.Key.CarteraID}", s.NombreCartera(g.Key.CarteraID),
                    unico, g.Key.CarteraID, esPar: unico is not null, s, catalogo, suma));
            }
        }
        else
        {
            // Por líder con titulares distintos.
            foreach (var r in s.FuentesLider.Where(r => r.LiderID != 0 && (liderID is null || r.LiderID == liderID)))
                filas.Add(FilaCobertura($"l{r.LiderID}", s.NombreLider(r.LiderID), r.LiderID, null, esPar: false, s, catalogo, r));
        }

        var orden = catalogo.Select((f, i) => (f.Clave, i)).ToDictionary(x => x.Clave, x => x.i, StringComparer.OrdinalIgnoreCase);
        return new CoberturaLocalizacionDto
        {
            Corte   = s.Corte,
            Agrupar = agrupar,
            Filas   = filas
                .OrderBy(f => f.EtiquetaGrupo, StringComparer.Create(Co, true))
                .ThenBy(f => orden.GetValueOrDefault(f.Fuente.Clave, 99))
                .ToList(),
        };
    }

    private static FilaCoberturaDto FilaCobertura(
        string grupo, string etiqueta, int? liderID, int? carteraID, bool esPar,
        Snapshot s, IReadOnlyList<FuenteCatalogo> catalogo, FilaFuente r)
    {
        var fuente = MapearFuente(r, catalogo);
        return new FilaCoberturaDto
        {
            Grupo           = grupo,
            EtiquetaGrupo   = etiqueta,
            LiderID         = liderID,
            NombreLider     = liderID is { } l ? s.NombreLider(l) : null,
            DocumentoLider  = liderID is { } l2 ? s.DocumentoLider(l2) : null,
            CarteraID       = carteraID,
            NombreCartera   = carteraID is { } c ? s.NombreCartera(c) : null,
            EsPar           = esPar,
            Fuente          = fuente,
            PctCobertura    = CalculoLocalizacion.Pct(r.Buscados, r.Total),
            PctExito        = CalculoLocalizacion.Pct(r.Encontrados, r.Buscados),
            PctLocalizacion = CalculoLocalizacion.Pct(r.Encontrados, r.Total),
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Frescura
    // ════════════════════════════════════════════════════════════════════════

    public async Task<FrescuraLocalizacionDto> ObtenerFrescuraAsync(
        int? liderID, int? carteraID, int umbral, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var catalogo = await CatalogoAsync(ct);
        var alcance = ResolverAlcance(s, liderID, carteraID);
        umbral = umbral switch { 90 => 90, 365 => 365, _ => 180 };

        var filas = alcance.PorFuente
            .Select(r =>
            {
                var cat = BuscarCatalogo(catalogo, r.Fuente);
                var viejos = umbral switch { 90 => r.Viejos90, 365 => r.Viejos365, _ => r.Viejos180 };
                return new FilaFrescuraDto
                {
                    Clave             = r.Fuente,
                    Etiqueta          = cat?.Etiqueta ?? r.Fuente,
                    Estado            = cat?.Estado ?? EstadoFuente.Inactivo,
                    Total             = r.Total,
                    Buscados          = r.Buscados,
                    Hasta90           = r.Buscados - r.Viejos90,
                    De91a180          = r.Viejos90 - r.Viejos180,
                    De181a365         = r.Viejos180 - r.Viejos365,
                    Mas365            = r.Viejos365,
                    Viejos            = viejos,
                    PctViejos         = CalculoLocalizacion.Pct(viejos, r.Buscados),
                    UltimaConsultaMax = r.UltimaConsultaMax,
                    UltimoCargueOk    = cat?.UltimoCargueOk,
                };
            })
            .OrderBy(f => OrdenCatalogo(catalogo, f.Clave))
            .ToList();

        return new FrescuraLocalizacionDto
        {
            Corte           = s.Corte,
            Alcance         = alcance.Tipo,
            EtiquetaAlcance = alcance.Etiqueta,
            Umbral          = umbral,
            Fuentes         = filas,
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Rankings
    // ════════════════════════════════════════════════════════════════════════

    public async Task<RankingsLocalizacionDto> ObtenerRankingsAsync(
        CriterioRankingLocalizacion criterio, bool incluirExternas, int? liderPropio, CancellationToken ct = default)
    {
        var s = await ObtenerSnapshotAsync(ct);
        var esLider = liderPropio is not null;
        if (esLider) incluirExternas = false;   // el interruptor es solo de Admin/Director
        var soloInternas = !incluirExternas;
        var menorEsMejor = CalculoLocalizacion.MenorEsMejor(criterio);

        decimal? Valor(MetricasLocalizacion m) => CalculoLocalizacion.ValorCriterio(m, criterio, _opciones);

        // ── Líderes: titulares distintos; sin externas ni BASE GENERAL salvo el interruptor ──
        var anteriores = s.LideresAnterior
            .Where(l => l.SoloInternas == soloInternas)
            .ToDictionary(l => l.LiderID, l => Valor(l.Metricas));

        var candidatos = s.Lideres
            .Where(l => l.LiderID != 0 && l.SoloInternas == soloInternas && l.Metricas.Total > 0)
            .ToList();

        var ranking = CalculoLocalizacion.Rankear(candidatos, l => Valor(l.Metricas), l => s.NombreLider(l.LiderID), menorEsMejor);

        IReadOnlyList<FilaRankeada<FilaLider>> visibles = ranking;
        int? miPosicion = null;
        var recortado = false;
        if (esLider)
        {
            var recorte = CalculoLocalizacion.RecortarParaLider(ranking, l => l.LiderID == liderPropio, _opciones.TopRankingLider);
            visibles = recorte.Filas;
            miPosicion = recorte.MiPosicion;
            recortado = recorte.Recortado;
        }

        var lideres = visibles.Select(f => new FilaRankingLiderDto
        {
            Posicion            = f.Posicion,
            LiderID             = f.Item.LiderID,
            Nombre              = s.NombreLider(f.Item.LiderID),
            Documento           = s.DocumentoLider(f.Item.LiderID),
            Valor               = f.Valor,
            Delta               = Delta(f.Valor, anteriores.GetValueOrDefault(f.Item.LiderID)),
            Total               = f.Item.Metricas.Total,
            Indice              = CalculoLocalizacion.Indice(f.Item.Metricas, _opciones),
            PctContacto         = CalculoLocalizacion.PctContacto(f.Item.Metricas),
            PctCobertura        = CalculoLocalizacion.PctCobertura(f.Item.Metricas),
            PctFrescura         = CalculoLocalizacion.PctFrescura(f.Item.Metricas),
            SinContactoConSaldo = f.Item.Metricas.SinContactoConSaldo,
            SaldoSinContacto    = f.Item.Metricas.SaldoSinContacto,
            EsPropio            = f.Item.LiderID == liderPropio,
        }).ToList();

        // ── Carteras: Admin/Director ven la cartera completa; el Líder, solo sus pares ──
        List<FilaRankingCarteraDto> carteras;
        if (esLider)
        {
            var propios = s.Pares.Where(p => p.LiderID == liderPropio && p.Metricas.Total > 0).ToList();
            var anterioresPar = s.ParesAnterior.Where(p => p.LiderID == liderPropio)
                .ToDictionary(p => p.CarteraID, p => Valor(p.Metricas));
            carteras = CalculoLocalizacion
                .Rankear(propios, p => Valor(p.Metricas), p => s.NombreCartera(p.CarteraID), menorEsMejor)
                .Select(f => FilaCartera(f.Posicion, f.Item.CarteraID, s.NombreCartera(f.Item.CarteraID), f.Item.LiderID, 1,
                                         f.Valor, anterioresPar.GetValueOrDefault(f.Item.CarteraID), f.Item.Metricas))
                .ToList();
        }
        else
        {
            static Dictionary<int, (MetricasLocalizacion M, int Lideres)> PorCartera(IEnumerable<FilaPar> pares, bool soloInternas) =>
                pares.Where(p => !soloInternas || p.Interna)
                     .GroupBy(p => p.CarteraID)
                     .ToDictionary(g => g.Key, g => (MetricasLocalizacion.Sumar(g.Select(p => p.Metricas)), g.Count()));

            var actuales = PorCartera(s.Pares, soloInternas).Where(kv => kv.Value.M.Total > 0).ToList();
            var previas = PorCartera(s.ParesAnterior, soloInternas);
            carteras = CalculoLocalizacion
                .Rankear(actuales, kv => Valor(kv.Value.M), kv => s.NombreCartera(kv.Key), menorEsMejor)
                .Select(f => FilaCartera(f.Posicion, f.Item.Key, s.NombreCartera(f.Item.Key), null, f.Item.Value.Lideres,
                                         f.Valor, previas.TryGetValue(f.Item.Key, out var p) ? Valor(p.M) : null, f.Item.Value.M))
                .ToList();
        }

        return new RankingsLocalizacionDto
        {
            Corte              = s.Corte,
            CorteAnterior      = s.CorteAnterior,
            Criterio           = criterio.ToString(),
            MenorEsMejor       = menorEsMejor,
            IncluirExternas    = incluirExternas,
            Lideres            = lideres,
            TotalLideres       = ranking.Count,
            MiPosicion         = miPosicion,
            Recortado          = recortado,
            Carteras           = carteras,
            CarterasSonPropias = esLider,
        };
    }

    private FilaRankingCarteraDto FilaCartera(
        int posicion, int carteraID, string nombre, int? liderID, int lideres, decimal? valor, decimal? anterior, MetricasLocalizacion m) =>
        new()
        {
            Posicion            = posicion,
            CarteraID           = carteraID,
            Nombre              = nombre,
            LiderID             = liderID,
            Lideres             = lideres,
            Valor               = valor,
            Delta               = Delta(valor, anterior),
            Total               = m.Total,
            Indice              = CalculoLocalizacion.Indice(m, _opciones),
            PctContacto         = CalculoLocalizacion.PctContacto(m),
            PctCobertura        = CalculoLocalizacion.PctCobertura(m),
            PctFrescura         = CalculoLocalizacion.PctFrescura(m),
            SinContactoConSaldo = m.SinContactoConSaldo,
            SaldoSinContacto    = m.SaldoSinContacto,
        };

    private static decimal? Delta(decimal? actual, decimal? anterior) =>
        actual is null || anterior is null
            ? null
            : Math.Round(actual.Value - Math.Round(anterior.Value, 1, MidpointRounding.AwayFromZero), 1, MidpointRounding.AwayFromZero);

    // ════════════════════════════════════════════════════════════════════════
    //  Alcance
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Universo pedido: métricas, filas por fuente y pares que lo componen.</summary>
    private sealed record Alcance(
        string Tipo, string Etiqueta, MetricasLocalizacion Metricas, IReadOnlyList<FilaFuente> PorFuente, IReadOnlyList<FilaPar> Pares);

    private static Alcance ResolverAlcance(Snapshot s, int? liderID, int? carteraID)
    {
        if (liderID is { } l && carteraID is { } c)
        {
            var par = s.Pares.FirstOrDefault(p => p.LiderID == l && p.CarteraID == c);
            return new Alcance("par", $"{s.NombreCartera(c)} — {s.NombreLider(l)}",
                par?.Metricas ?? MetricasLocalizacion.Vacias,
                s.Fuentes.Where(r => r.LiderID == l && r.CarteraID == c).ToList(),
                par is null ? [] : [par]);
        }

        if (liderID is { } soloL)
        {
            var lider = s.Lideres.FirstOrDefault(x => x.LiderID == soloL && !x.SoloInternas);
            return new Alcance("lider", s.NombreLider(soloL),
                lider?.Metricas ?? MetricasLocalizacion.Vacias,
                s.FuentesLider.Where(r => r.LiderID == soloL).ToList(),
                s.Pares.Where(p => p.LiderID == soloL).ToList());
        }

        if (carteraID is { } soloC)
        {
            var pares = s.Pares.Where(p => p.CarteraID == soloC).ToList();
            return new Alcance("cartera", s.NombreCartera(soloC),
                MetricasLocalizacion.Sumar(pares.Select(p => p.Metricas)),
                s.Fuentes.Where(r => r.CarteraID == soloC).GroupBy(r => r.Fuente).Select(FilaFuente.Sumar).ToList(),
                pares);
        }

        var global = s.Lideres.FirstOrDefault(x => x.LiderID == 0 && !x.SoloInternas);
        return new Alcance("global", "Todos los líderes y carteras",
            global?.Metricas ?? MetricasLocalizacion.Vacias,
            s.FuentesLider.Where(r => r.LiderID == 0).ToList(),
            s.Pares);
    }

    private static List<FuenteResumenDto> FuentesDelAlcance(Snapshot s, Alcance a, IReadOnlyList<FuenteCatalogo> catalogo) =>
        a.PorFuente
            .Select(r => MapearFuente(r, catalogo))
            .OrderBy(f => OrdenCatalogo(catalogo, f.Clave))
            .ToList();

    private static FuenteResumenDto MapearFuente(FilaFuente r, IReadOnlyList<FuenteCatalogo> catalogo)
    {
        var cat = BuscarCatalogo(catalogo, r.Fuente);
        var noEnc = r.Buscados - r.Encontrados;
        var falta = r.Total - r.Buscados;
        return new FuenteResumenDto
        {
            Clave             = r.Fuente,
            Etiqueta          = cat?.Etiqueta ?? r.Fuente,
            Slug              = cat?.Slug ?? string.Empty,
            Estado            = cat?.Estado ?? EstadoFuente.Inactivo,
            Grupo             = cat?.Grupo ?? GrupoFuente.Contacto,
            Nota              = cat?.Nota,
            Total             = r.Total,
            Buscados          = r.Buscados,
            Encontrados       = r.Encontrados,
            NoEncontrados     = noEnc,
            FaltaPorBuscar    = falta,
            PctEncontrados    = CalculoLocalizacion.Pct(r.Encontrados, r.Total),
            PctNoEncontrados  = CalculoLocalizacion.Pct(noEnc, r.Total),
            PctFaltaPorBuscar = CalculoLocalizacion.Pct(falta, r.Total),
            UltimaConsultaMax = r.UltimaConsultaMax,
            UltimoCargueOk    = cat?.UltimoCargueOk,
        };
    }

    private MetricasLocalizacionDto MapearMetricas(MetricasLocalizacion m) => new()
    {
        Total                  = m.Total,
        ConContacto            = m.ConContacto,
        SinContacto            = m.Total - m.ConContacto,
        ConContactoFuncional   = m.ConContactoFuncional,
        BuscadosAlguna         = m.BuscadosAlguna,
        BuscadosFuncional      = m.BuscadosFuncional,
        NuncaBuscadosFuncional = m.Total - m.BuscadosFuncional,
        Frescos180             = m.Frescos180,
        ConSaldo               = m.ConSaldo,
        SinContactoConSaldo    = m.SinContactoConSaldo,
        SaldoSinContacto       = m.SaldoSinContacto,
        SaldoTotal             = m.SaldoTotal,
        PctContacto            = CalculoLocalizacion.PctContacto(m),
        PctCobertura           = CalculoLocalizacion.PctCobertura(m),
        PctFrescura            = CalculoLocalizacion.PctFrescura(m),
        PctSinContactoConSaldo = CalculoLocalizacion.PctSinContactoConSaldo(m),
        Indice                 = CalculoLocalizacion.Indice(m, _opciones),
    };

    // ════════════════════════════════════════════════════════════════════════
    //  Catálogo de fuentes (estado + último cargue OK)
    // ════════════════════════════════════════════════════════════════════════

    private sealed record FuenteCatalogo(
        string Clave, string Etiqueta, string Slug, EstadoFuente Estado, GrupoFuente Grupo, string? Nota, DateTime? UltimoCargueOk);

    /// <summary>Las 21 fuentes del panel (CamaraEmpresa no es por titular), en el orden del Calificador.</summary>
    private async Task<IReadOnlyList<FuenteCatalogo>> CatalogoAsync(CancellationToken ct)
    {
        var lista = await _fuentes.ListarAsync(ct);
        return lista
            .Where(f => f.EnPanelCalificador)
            .Select(f => new FuenteCatalogo(f.Clave, f.EtiquetaCalificador, f.Slug, f.Estado, f.Grupo, f.Nota, f.UltimoCargueOk))
            .ToList();
    }

    private static FuenteCatalogo? BuscarCatalogo(IReadOnlyList<FuenteCatalogo> catalogo, string clave) =>
        catalogo.FirstOrDefault(f => f.Clave.Equals(clave, StringComparison.OrdinalIgnoreCase));

    private static int OrdenCatalogo(IReadOnlyList<FuenteCatalogo> catalogo, string clave)
    {
        for (var i = 0; i < catalogo.Count; i++)
            if (catalogo[i].Clave.Equals(clave, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Snapshot del corte
    // ════════════════════════════════════════════════════════════════════════

    internal sealed record FilaFuente(
        int LiderID, int CarteraID, string Fuente, int Total, int Buscados, int Encontrados,
        int Viejos90, int Viejos180, int Viejos365, DateTime? UltimaConsultaMax)
    {
        /// <summary>Suma de filas de la misma fuente sobre universos disjuntos (pares de una cartera).</summary>
        public static FilaFuente Sumar(IEnumerable<FilaFuente> filas)
        {
            var l = filas.ToList();
            return new FilaFuente(
                l.Select(x => x.LiderID).Distinct().Count() == 1 ? l[0].LiderID : 0,
                l.Select(x => x.CarteraID).Distinct().Count() == 1 ? l[0].CarteraID : 0,
                l[0].Fuente,
                l.Sum(x => x.Total), l.Sum(x => x.Buscados), l.Sum(x => x.Encontrados),
                l.Sum(x => x.Viejos90), l.Sum(x => x.Viejos180), l.Sum(x => x.Viejos365),
                l.Max(x => x.UltimaConsultaMax));
        }
    }

    internal sealed record FilaPar(int LiderID, int CarteraID, bool Interna, MetricasLocalizacion Metricas);
    internal sealed record FilaLider(int LiderID, bool SoloInternas, MetricasLocalizacion Metricas);

    internal sealed class Snapshot
    {
        public required CorteLocalizacionDto Corte { get; init; }
        public CorteLocalizacionDto? CorteAnterior { get; init; }
        public required List<FilaFuente> Fuentes { get; init; }
        /// <summary>Por líder y fuente (titulares distintos); LiderID 0 = todos, CarteraID siempre 0.</summary>
        public required List<FilaFuente> FuentesLider { get; init; }
        public required List<FilaPar> Pares { get; init; }
        public required List<FilaLider> Lideres { get; init; }
        public required List<FilaPar> ParesAnterior { get; init; }
        public required List<FilaLider> LideresAnterior { get; init; }
        public required Dictionary<int, (string Nombre, long? Documento)> LideresInfo { get; init; }
        public required Dictionary<int, string> CarterasInfo { get; init; }

        public string NombreLider(int id) => LideresInfo.TryGetValue(id, out var l) ? l.Nombre : $"Líder {id}";
        public long? DocumentoLider(int id) => LideresInfo.TryGetValue(id, out var l) ? l.Documento : null;
        public string NombreCartera(int id) => CarterasInfo.TryGetValue(id, out var c) ? c : $"Cartera {id}";
    }

    private async Task<Snapshot> ObtenerSnapshotAsync(CancellationToken ct)
    {
        var clave = $"localizacion:snapshot:{_coordinador.Version}";
        if (_cache.TryGetValue(clave, out Snapshot? enCache) && enCache is not null)
        {
            _coordinador.VerificarVigencia(enCache.Corte.FechaCorte);
            return enCache;
        }

        var s = await CargarSnapshotAsync(ct);
        _coordinador.VerificarVigencia(s?.Corte.FechaCorte);
        if (s is null) throw new LocalizacionSinCorteException(_coordinador.Calculando);

        _cache.Set(clave, s, TimeSpan.FromMinutes(Math.Max(1, _opciones.MinutosCacheLectura)));
        return s;
    }

    private const string ColumnasCorte =
        "CorteID, FechaCorte, Motivo, Usuario, Estado, DuracionMs, Pares, Titulares, DatosGestionAl, Error";

    private const string ColumnasMetricas =
        "Total, ConContacto, ConContactoFuncional, BuscadosAlguna, BuscadosFuncional, Frescos180, "
      + "ConSaldo, SinContactoConSaldo, SaldoSinContacto, SaldoTotal";

    private async Task<Snapshot?> CargarSnapshotAsync(CancellationToken ct)
    {
        await using var conn = await AbrirAsync(ct);
        await using var cmd = new SqlCommand($"""
            SET NOCOUNT ON;
            DECLARE @CorteID int, @FechaCorte datetime2(0), @Anterior int;
            SELECT TOP (1) @CorteID = CorteID, @FechaCorte = FechaCorte
              FROM dbo.LocalizacionCorte WHERE Estado = 'OK' ORDER BY CorteID DESC;
            -- Variaciones contra el último corte OK de un día anterior (no contra el de hace una hora).
            SELECT TOP (1) @Anterior = CorteID
              FROM dbo.LocalizacionCorte
             WHERE Estado = 'OK' AND FechaCorte < CAST(CAST(@FechaCorte AS date) AS datetime2(0))
             ORDER BY CorteID DESC;

            SELECT {ColumnasCorte} FROM dbo.LocalizacionCorte WHERE CorteID = @CorteID;
            SELECT {ColumnasCorte} FROM dbo.LocalizacionCorte WHERE CorteID = @Anterior;
            SELECT LiderID, CarteraID, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax
              FROM dbo.LocalizacionResumenFuente WHERE CorteID = @CorteID;
            SELECT LiderID, 0, Fuente, Total, Buscados, Encontrados, Viejos90, Viejos180, Viejos365, UltimaConsultaMax
              FROM dbo.LocalizacionResumenFuenteLider WHERE CorteID = @CorteID;
            SELECT CorteID, LiderID, CarteraID, Interna, {ColumnasMetricas}
              FROM dbo.LocalizacionResumenPar WHERE CorteID IN (@CorteID, @Anterior);
            SELECT CorteID, LiderID, SoloInternas, {ColumnasMetricas}
              FROM dbo.LocalizacionResumenLider WHERE CorteID IN (@CorteID, @Anterior);
            SELECT ID, Nombre, NumeroDocumento FROM dbo.Lider;
            SELECT ID, NombreCartera FROM dbo.Cartera;
            """, conn);

        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);

            if (!await rd.ReadAsync(ct)) return null;          // sin cortes OK
            var corte = LeerCorte(rd);

            await rd.NextResultAsync(ct);
            var anterior = await rd.ReadAsync(ct) ? LeerCorte(rd) : null;

            await rd.NextResultAsync(ct);
            var fuentes = await LeerFuentesAsync(rd, ct);

            await rd.NextResultAsync(ct);
            var fuentesLider = await LeerFuentesAsync(rd, ct);

            await rd.NextResultAsync(ct);
            List<FilaPar> pares = [], paresAnt = [];
            while (await rd.ReadAsync(ct))
            {
                var fila = new FilaPar(rd.GetInt32(1), rd.GetInt32(2), rd.GetBoolean(3), LeerMetricas(rd, 4));
                (rd.GetInt32(0) == corte.CorteID ? pares : paresAnt).Add(fila);
            }

            await rd.NextResultAsync(ct);
            List<FilaLider> lideres = [], lideresAnt = [];
            while (await rd.ReadAsync(ct))
            {
                var fila = new FilaLider(rd.GetInt32(1), rd.GetBoolean(2), LeerMetricas(rd, 3));
                (rd.GetInt32(0) == corte.CorteID ? lideres : lideresAnt).Add(fila);
            }

            await rd.NextResultAsync(ct);
            var lideresInfo = new Dictionary<int, (string, long?)>();
            while (await rd.ReadAsync(ct))
                lideresInfo[rd.GetInt32(0)] = (rd.GetString(1).Trim(), rd.IsDBNull(2) ? null : rd.GetInt64(2));

            await rd.NextResultAsync(ct);
            var carterasInfo = new Dictionary<int, string>();
            while (await rd.ReadAsync(ct))
                carterasInfo[rd.GetInt32(0)] = rd.GetString(1).Trim();

            return new Snapshot
            {
                Corte = corte, CorteAnterior = anterior,
                Fuentes = fuentes, FuentesLider = fuentesLider,
                Pares = pares, Lideres = lideres, ParesAnterior = paresAnt, LideresAnterior = lideresAnt,
                LideresInfo = lideresInfo, CarterasInfo = carterasInfo,
            };
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // 208 = "Invalid object name": el usuario todavía no aplicó el script.
            throw new LocalizacionTablasFaltantesException();
        }
    }

    private static async Task<List<FilaFuente>> LeerFuentesAsync(SqlDataReader rd, CancellationToken ct)
    {
        var filas = new List<FilaFuente>();
        while (await rd.ReadAsync(ct))
            filas.Add(new FilaFuente(
                rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2),
                rd.GetInt32(3), rd.GetInt32(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7), rd.GetInt32(8),
                rd.IsDBNull(9) ? null : rd.GetDateTime(9)));
        return filas;
    }

    private static MetricasLocalizacion LeerMetricas(SqlDataReader rd, int i) => new(
        rd.GetInt32(i), rd.GetInt32(i + 1), rd.GetInt32(i + 2), rd.GetInt32(i + 3), rd.GetInt32(i + 4),
        rd.GetInt32(i + 5), rd.GetInt32(i + 6), rd.GetInt32(i + 7), rd.GetDecimal(i + 8), rd.GetDecimal(i + 9));

    private static CorteLocalizacionDto LeerCorte(SqlDataReader rd) => new()
    {
        CorteID        = rd.GetInt32(0),
        FechaCorte     = rd.GetDateTime(1),
        Motivo         = rd.GetString(2),
        Usuario        = rd.IsDBNull(3) ? null : rd.GetString(3),
        Estado         = rd.GetString(4),
        DuracionMs     = rd.IsDBNull(5) ? null : rd.GetInt32(5),
        Pares          = rd.IsDBNull(6) ? null : rd.GetInt32(6),
        Titulares      = rd.IsDBNull(7) ? null : rd.GetInt32(7),
        DatosGestionAl = rd.IsDBNull(8) ? null : rd.GetDateTime(8),
        Error          = rd.IsDBNull(9) ? null : rd.GetString(9),
    };

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        return conn;
    }
}

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Ranking;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Calcula la categoría de ranking (Diamante/Oro/Plata/Bronce) de todos los agentes
/// internos para un año/mes, en un solo recorrido — antes este cálculo vivía duplicado
/// dentro de DashboardAsesorService y se repetía por cada asesor que abría su dashboard
/// (cada uno recalculaba el Top 20 de toda la empresa para descartar todo menos su
/// propia fila). Ahora se calcula una vez por mes y se cachea (ICategoriaRankingCache).
///
/// TEMPORAL: regla de negocio en definición con el líder — sin persistencia.
/// Categoría base = promedio de honorarios del año en curso (meses ya cargados).
///   - Top 20 asesores internos de toda la empresa por ese promedio → Diamante.
///   - Resto por rango: Oro >= 6.000.000, Plata >= 4.000.000, Bronce < 4.000.000.
/// Ajuste: si el honorario del mes de referencia es MENOR al del mes inmediato
/// anterior (bajo rendimiento), la categoría final se recalcula con el valor de
/// ESE mes (Top 20 + rangos) en vez del promedio anual. Si el mes de referencia no
/// tiene ningún pago cargado aún (no confundir con "honorarios en 0"), no aplica el ajuste.
/// "Asesor Externo" se excluye vía LiderCartera.EsExterna (mismo patrón que
/// InformesRecaudoService: universo administrativo = combinaciones no externas).
/// </summary>
public class CategoriaRankingService : ICategoriaRankingService
{
    private readonly CallCenterDbContext _callCenter;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly ICategoriaRankingCache _cache;

    // Un semáforo por (año, mes) para que, si varias requests piden el mismo mes justo
    // después de una invalidación (cache miss simultáneo), solo una calcule y las demás
    // esperen y lean el resultado ya cacheado — evita duplicar el escaneo de toda la
    // empresa contra la BD. Static: debe compartirse entre requests aunque el servicio
    // sea Scoped (una instancia nueva por request).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _candados = new();

    public CategoriaRankingService(
        CallCenterDbContext callCenter,
        RecursosHumanosDbContext rrhh,
        ICategoriaRankingCache cache)
    {
        _callCenter = callCenter;
        _rrhh       = rrhh;
        _cache      = cache;
    }

    public async Task<Dictionary<long, CategoriaAgenteDto>> ObtenerCategoriasAsync(int año, int mes)
    {
        if (_cache.TryGet(año, mes, out var cached) && cached is not null)
            return cached;

        var candado = _candados.GetOrAdd($"{año}:{mes}", _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync();
        try
        {
            // Doble chequeo: mientras esperábamos el candado, otra request pudo haber
            // terminado de calcular y dejar el resultado ya listo en el caché.
            if (_cache.TryGet(año, mes, out cached) && cached is not null)
                return cached;

            return await CalcularYCachearAsync(año, mes);
        }
        finally
        {
            candado.Release();
        }
    }

    private async Task<Dictionary<long, CategoriaAgenteDto>> CalcularYCachearAsync(int año, int mes)
    {
        var (añoAnterior, mesAnterior) = mes == 1 ? (año - 1, 12) : (año, mes - 1);

        var honorariosPorAgenteMes = await _callCenter.Pagos
            .Where(p => p.AgenteControlID.HasValue
                     && !_callCenter.LiderCarteras.Any(lc =>
                            lc.LiderID == p.LiderID && lc.CarteraID == p.CarteraID && lc.EsExterna)
                     && ((p.FechaPago.Year == año)
                         || (p.FechaPago.Year == añoAnterior && p.FechaPago.Month == mesAnterior)))
            .GroupBy(p => new { p.AgenteControlID, p.FechaPago.Year, p.FechaPago.Month })
            .Select(g => new
            {
                AgenteID   = g.Key.AgenteControlID!.Value,
                g.Key.Year,
                g.Key.Month,
                Honorarios = g.Sum(p => p.Honorarios),
            })
            .AsNoTracking()
            .ToListAsync();

        var promedioPorAgente = honorariosPorAgenteMes
            .Where(x => x.Year == año)
            .GroupBy(x => x.AgenteID)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Honorarios));

        var top20PorPromedio = promedioPorAgente
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => kv.Key)
            .ToHashSet();

        var honorariosMesActualPorAgente = honorariosPorAgenteMes
            .Where(x => x.Year == año && x.Month == mes)
            .ToDictionary(x => x.AgenteID, x => x.Honorarios);

        var honorariosMesAnteriorPorAgente = honorariosPorAgenteMes
            .Where(x => x.Year == añoAnterior && x.Month == mesAnterior)
            .ToDictionary(x => x.AgenteID, x => x.Honorarios);

        var top20PorMesActual = honorariosMesActualPorAgente
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => kv.Key)
            .ToHashSet();

        var agenteIDs = promedioPorAgente.Keys.ToList();
        var empleados = await _rrhh.Empleados
            .Where(e => agenteIDs.Contains(e.NumeroDocumento))
            .AsNoTracking()
            .ToDictionaryAsync(e => e.NumeroDocumento, e => $"{e.Nombre} {e.Apellidos}".Trim());

        var resultado = new Dictionary<long, CategoriaAgenteDto>();
        foreach (var agenteID in agenteIDs)
        {
            var promedioAgente = promedioPorAgente[agenteID];
            var categoriaPorPromedio = top20PorPromedio.Contains(agenteID)
                ? "Diamante"
                : CategoriaPorRango(promedioAgente);

            decimal? honorarioMesActual = honorariosMesActualPorAgente.TryGetValue(agenteID, out var hma1) ? hma1 : null;
            decimal? honorarioMesAnterior = honorariosMesAnteriorPorAgente.TryGetValue(agenteID, out var hma2) ? hma2 : null;

            var ajuste = honorarioMesAnterior is not null
                      && honorarioMesActual is not null
                      && honorarioMesActual < honorarioMesAnterior;

            var categoriaFinal = ajuste
                ? (top20PorMesActual.Contains(agenteID) ? "Diamante" : CategoriaPorRango(honorarioMesActual!.Value))
                : categoriaPorPromedio;

            resultado[agenteID] = new CategoriaAgenteDto
            {
                AgenteID                 = agenteID,
                NombreAgente             = empleados.TryGetValue(agenteID, out var nombre) ? nombre : $"Agente {agenteID}",
                Categoria                = categoriaFinal,
                CategoriaPorPromedio     = categoriaPorPromedio,
                PromedioAnual            = promedioAgente,
                HonorarioMesActual       = honorarioMesActual,
                HonorarioMesAnterior     = honorarioMesAnterior,
                AjustePorBajoRendimiento = ajuste,
            };
        }

        _cache.Set(año, mes, resultado);
        return resultado;
    }

    internal static string CategoriaPorRango(decimal honorarios) =>
        honorarios >= 6_000_000m ? "Oro" :
        honorarios >= 4_000_000m ? "Plata" :
        "Bronce";
}

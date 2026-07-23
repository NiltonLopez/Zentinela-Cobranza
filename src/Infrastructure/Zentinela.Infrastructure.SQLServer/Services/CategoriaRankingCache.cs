using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Zentinela.Application.DTOs.Ranking;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Implementación Singleton del caché de categorías de ranking. Mismo mecanismo que
/// <see cref="CalificadorCarteraCache"/>: un CancellationTokenSource compartido como
/// expiration token, para invalidación global O(1) al cancelarlo.
///
/// A diferencia del Calificador, aquí no hay una carga nocturna externa que limpiar
/// oportunistamente — la invalidación la dispara explícitamente CarguePagosService
/// justo después de confirmar un cargue de pagos (masivo, estándar o individual).
/// El TTL es solo una red de seguridad por si algún cargue no invalida correctamente.
/// </summary>
public class CategoriaRankingCache : ICategoriaRankingCache
{
    private readonly IMemoryCache _cache;
    private CancellationTokenSource _resetCts = new();
    private readonly object _lock = new();

    private static readonly TimeSpan ExpiracionAbsoluta = TimeSpan.FromHours(6);
    private static readonly TimeSpan ExpiracionSliding  = TimeSpan.FromHours(1);

    public CategoriaRankingCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(int año, int mes, out Dictionary<long, CategoriaAgenteDto>? categorias)
    {
        if (_cache.TryGetValue(BuildKey(año, mes), out Dictionary<long, CategoriaAgenteDto>? cached)
            && cached is not null)
        {
            categorias = cached;
            return true;
        }

        categorias = null;
        return false;
    }

    public void Set(int año, int mes, Dictionary<long, CategoriaAgenteDto> categorias)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            cts = _resetCts;
        }

        var opts = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ExpiracionAbsoluta,
            SlidingExpiration               = ExpiracionSliding,
        }.AddExpirationToken(new CancellationChangeToken(cts.Token));

        _cache.Set(BuildKey(año, mes), categorias, opts);
    }

    public void InvalidarTodo()
    {
        CancellationTokenSource toCancel;
        lock (_lock)
        {
            toCancel = _resetCts;
            _resetCts = new CancellationTokenSource();
        }

        toCancel.Cancel();
        toCancel.Dispose();
    }

    private static string BuildKey(int año, int mes) => $"categoria-ranking:{año}:{mes}";
}

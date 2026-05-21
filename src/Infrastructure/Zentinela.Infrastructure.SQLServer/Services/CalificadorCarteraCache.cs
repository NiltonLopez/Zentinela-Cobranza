using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Implementación Singleton del caché del Calificador. Envuelve <see cref="IMemoryCache"/>
/// (también Singleton) y usa un <see cref="CancellationTokenSource"/> compartido como
/// expiration token para soportar invalidación global O(1): al cancelar el CTS, todas
/// las entradas adjuntas expiran automáticamente. Tras la cancelación se instancia un
/// CTS nuevo para que las siguientes entradas no nazcan ya canceladas.
/// </summary>
public class CalificadorCarteraCache : ICalificadorCarteraCache
{
    private readonly IMemoryCache _cache;
    private CancellationTokenSource _resetCts = new();
    private readonly object _lock = new();

    private static readonly TimeSpan ExpiracionAbsoluta = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExpiracionSliding  = TimeSpan.FromMinutes(5);

    public CalificadorCarteraCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(int carteraID, int? liderID, out CalificadorCarteraDto? dto)
    {
        if (_cache.TryGetValue(BuildKey(carteraID, liderID), out CalificadorCarteraDto? cached)
            && cached is not null)
        {
            dto = cached;
            return true;
        }

        dto = null;
        return false;
    }

    public void Set(int carteraID, int? liderID, CalificadorCarteraDto dto)
    {
        // Snapshot del CTS actual bajo lock para evitar adjuntar un token de un CTS
        // que está siendo reemplazado por InvalidarTodo() en otro hilo.
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

        _cache.Set(BuildKey(carteraID, liderID), dto, opts);
    }

    public void InvalidarTodo()
    {
        // Intercambia el CTS actual y cancela el anterior — todas las entradas
        // adjuntas al CTS viejo expiran inmediatamente. Las nuevas Set() usarán el CTS nuevo.
        CancellationTokenSource toCancel;
        lock (_lock)
        {
            toCancel = _resetCts;
            _resetCts = new CancellationTokenSource();
        }

        toCancel.Cancel();
        toCancel.Dispose();
    }

    private static string BuildKey(int carteraID, int? liderID) =>
        $"calificador:{carteraID}:{(liderID?.ToString() ?? "null")}";
}

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
///
/// La fuente "localización" (Cajacopi, Fosyga, Tigo, etc.) se recarga de noche por un
/// proceso externo a esta app (no hay hook para invalidar justo cuando termina), así que
/// el TTL por entrada es largo (pensado para durar todo el día) y la frescura real la da
/// <see cref="VerificarLimpiezaDiaria"/>: sin usar un BackgroundService, cada TryGet/Set
/// revisa si ya pasó el corte diario (1pm) desde la última limpieza y, si es así, invalida
/// todo antes de continuar — se dispara solo con el tráfico normal del Calificador, no con
/// un temporizador aparte.
/// </summary>
public class CalificadorCarteraCache : ICalificadorCarteraCache
{
    private readonly IMemoryCache _cache;
    private CancellationTokenSource _resetCts = new();
    private readonly object _lock = new();

    // TTL largo: la frescura diaria la garantiza VerificarLimpiezaDiaria, no este valor.
    // Sirve solo de red de seguridad por si algún día no hay corte (ej. app caída a las 7am).
    private static readonly TimeSpan ExpiracionAbsoluta = TimeSpan.FromHours(24);
    private static readonly TimeSpan ExpiracionSliding  = TimeSpan.FromHours(2);

    // Hora del corte diario (después de que la carga nocturna de localización ya terminó).
    private static readonly TimeSpan HoraLimpiezaDiaria = TimeSpan.FromHours(13);
    private DateTime _ultimaLimpieza = DateTime.Now;

    public CalificadorCarteraCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(int carteraID, int? liderID, out CalificadorCarteraDto? dto)
    {
        VerificarLimpiezaDiaria();

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
        VerificarLimpiezaDiaria();

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

    public void InvalidarTodo() => InvalidarInterno();

    /// <summary>
    /// Sin BackgroundService: se evalúa oportunistamente en cada TryGet/Set. Calcula el
    /// último corte de la 1pm que ya pasó (hoy si ya es la 1pm, ayer si todavía no) y, si
    /// la limpieza más reciente es anterior a ese corte, invalida todo — como máximo una
    /// vez por día, disparado por el primer request que llegue después de la 1pm.
    /// </summary>
    private void VerificarLimpiezaDiaria()
    {
        var ahora = DateTime.Now;
        var ultimoCorte = ahora.Date.Add(HoraLimpiezaDiaria);
        if (ahora < ultimoCorte) ultimoCorte = ultimoCorte.AddDays(-1);

        if (_ultimaLimpieza >= ultimoCorte) return;

        lock (_lock)
        {
            if (_ultimaLimpieza >= ultimoCorte) return; // otro hilo ya limpió
            InvalidarInterno();
            _ultimaLimpieza = ahora;
        }
    }

    private void InvalidarInterno()
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

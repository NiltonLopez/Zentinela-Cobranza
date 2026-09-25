using Microsoft.Extensions.Caching.Memory;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Caché de miniaturas de fotos (Singleton). Usa un <see cref="MemoryCache"/> PROPIO y no el
/// <c>IMemoryCache</c> compartido de la app: aquí cada entrada declara su tamaño en bytes y el
/// caché tiene tope (<see cref="LimiteBytes"/>), y un caché con <c>SizeLimit</c> exige Size en
/// TODAS sus entradas, lo que rompería a los demás usuarios del compartido (Calificador, Ranking).
///
/// Guarda también el "no hay foto" (documento sin foto, no autorizado o imagen ilegible) durante
/// <see cref="DuracionSinFoto"/>: un ranking con 30 líderes sin foto pediría 30 × N tamaños a RRHH
/// en cada recarga.
/// </summary>
internal sealed class FotoPersonaCache : IDisposable
{
    /// <summary>64 MB: una miniatura de 160 px pesa ~6-10 KB; alcanza de sobra para ~400 personas × 4 tamaños.</summary>
    public const long LimiteBytes = 64L * 1024 * 1024;

    public static readonly TimeSpan DuracionFoto = TimeSpan.FromHours(12);
    public static readonly TimeSpan DuracionSinFoto = TimeSpan.FromMinutes(10);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = LimiteBytes });

    /// <summary>Miniatura guardada.</summary>
    public sealed record Miniatura(byte[] Jpeg, string ETag);

    private sealed record SinFoto;
    private static readonly SinFoto MarcaSinFoto = new();

    private static string ClaveFoto(long documento, int tam) => $"foto:{documento}:{tam}";

    // El "no hay foto" no depende del tamaño: una sola entrada por documento.
    private static string ClaveSinFoto(long documento) => $"foto:{documento}:sin";

    public bool TryGetFoto(long documento, int tam, out Miniatura? miniatura)
    {
        if (_cache.TryGetValue(ClaveFoto(documento, tam), out Miniatura? m) && m is not null)
        {
            miniatura = m;
            return true;
        }
        miniatura = null;
        return false;
    }

    public bool EstaMarcadoSinFoto(long documento) => _cache.TryGetValue(ClaveSinFoto(documento), out SinFoto? _);

    public void GuardarFoto(long documento, int tam, Miniatura miniatura) =>
        _cache.Set(ClaveFoto(documento, tam), miniatura, new MemoryCacheEntryOptions
        {
            // Size = bytes del JPEG (+ un poco por la clave y el ETag).
            Size              = miniatura.Jpeg.Length + 128,
            SlidingExpiration = DuracionFoto,
        });

    public void MarcarSinFoto(long documento) =>
        _cache.Set(ClaveSinFoto(documento), MarcaSinFoto, new MemoryCacheEntryOptions
        {
            Size                            = 64,
            AbsoluteExpirationRelativeToNow = DuracionSinFoto,
        });

    public void Dispose() => _cache.Dispose();
}

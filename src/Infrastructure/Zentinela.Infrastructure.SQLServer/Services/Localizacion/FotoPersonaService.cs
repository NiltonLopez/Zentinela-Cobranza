using Microsoft.Extensions.Logging;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.SQLServer.Services.Localizacion;

/// <summary>
/// Miniaturas de fotos de líderes y asesores (ADR-040). Flujo:
/// caché → ¿documento habilitado? → foto de RRHH → <see cref="MiniaturaFoto"/> → caché.
/// El "no hay foto" también se cachea (corto) para no golpear RRHH en cada avatar vacío.
/// </summary>
internal sealed class FotoPersonaService : IFotoPersonaService
{
    private readonly IFotoPersonaOrigen _origen;
    private readonly FotoPersonaCache _cache;
    private readonly ILogger<FotoPersonaService> _log;

    public FotoPersonaService(IFotoPersonaOrigen origen, FotoPersonaCache cache, ILogger<FotoPersonaService> log)
    {
        _origen = origen;
        _cache = cache;
        _log = log;
    }

    public async Task<(byte[] Jpeg, string ETag)?> ObtenerMiniaturaAsync(long documento, int tam, CancellationToken ct = default)
    {
        tam = MiniaturaFoto.NormalizarTamano(tam);
        if (documento <= 0) return null;

        if (_cache.TryGetFoto(documento, tam, out var enCache))
            return (enCache!.Jpeg, enCache.ETag);
        if (_cache.EstaMarcadoSinFoto(documento))
            return null;

        // Solo líderes y asesores activos: sin este filtro el endpoint serviría la foto de
        // cualquier empleado de RRHH a quien adivine una cédula.
        if (!await _origen.EsPersonaHabilitadaAsync(documento, ct))
        {
            _cache.MarcarSinFoto(documento);
            return null;
        }

        var foto = await _origen.LeerFotoAsync(documento, ct);
        if (foto is null)
        {
            _cache.MarcarSinFoto(documento);
            return null;
        }

        // Una imagen corrupta no debe tumbar el ranking: se trata como "sin foto".
        byte[]? jpeg = null;
        try
        {
            jpeg = MiniaturaFoto.Generar(foto.Contenido, tam);
            if (jpeg is null)
                _log.LogWarning("La foto {FotoID} ({ContentType}) no se pudo decodificar", foto.ID, foto.ContentType);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo generar la miniatura de la foto {FotoID} ({ContentType})", foto.ID, foto.ContentType);
        }

        if (jpeg is null)
        {
            _cache.MarcarSinFoto(documento);
            return null;
        }

        var miniatura = new FotoPersonaCache.Miniatura(jpeg, MiniaturaFoto.CalcularETag(foto.ID, foto.UploadedAt, tam));
        _cache.GuardarFoto(documento, tam, miniatura);
        return (miniatura.Jpeg, miniatura.ETag);
    }
}

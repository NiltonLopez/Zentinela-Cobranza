using Zentinela.Application.DTOs.Ranking;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Caché in-memory (Singleton) del resultado de ICategoriaRankingService, por año/mes.
/// Se invalida globalmente cada vez que se confirma un cargue de pagos (masivo,
/// estándar o individual) — mismo mecanismo que ICalificadorCarteraCache.
/// </summary>
public interface ICategoriaRankingCache
{
    bool TryGet(int año, int mes, out Dictionary<long, CategoriaAgenteDto>? categorias);
    void Set(int año, int mes, Dictionary<long, CategoriaAgenteDto> categorias);
    void InvalidarTodo();
}

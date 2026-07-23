using Zentinela.Application.DTOs.Ranking;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Calcula la categoría de ranking (Diamante/Oro/Plata/Bronce) de todos los agentes
/// internos para un año/mes dado, a partir de Pagos.Honorarios. Resultado cacheado
/// (ver ICategoriaRankingCache) e invalidado en cada cargue de pagos.
/// </summary>
public interface ICategoriaRankingService
{
    /// <summary>Clave: AgenteID (Pago.AgenteControlID).</summary>
    Task<Dictionary<long, CategoriaAgenteDto>> ObtenerCategoriasAsync(int año, int mes);
}

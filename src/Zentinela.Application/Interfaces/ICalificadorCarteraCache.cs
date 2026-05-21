using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Caché in-memory para los resultados del Calificador de Cartera. Singleton.
/// TTL: 10 min absoluto + 5 min sliding. Se invalida globalmente al cargar
/// reportes o pagos (los dos cargues que mutan métricas del Calificador).
/// </summary>
public interface ICalificadorCarteraCache
{
    bool TryGet(int carteraID, int? liderID, out CalificadorCarteraDto? dto);
    void Set(int carteraID, int? liderID, CalificadorCarteraDto dto);
    void InvalidarTodo();
}

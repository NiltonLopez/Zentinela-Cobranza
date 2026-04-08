using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface ICalificadorCarteraService
{
    /// <summary>
    /// Lista de combinaciones Cartera+Líder disponibles según el rol del usuario.
    /// Líder → solo sus asignaciones. Director/Admin → todas.
    /// </summary>
    Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(ContextoUsuario contexto);

    /// <summary>
    /// Calcula todas las métricas para una asignación Cartera+Líder específica.
    /// </summary>
    Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int liderID);
}

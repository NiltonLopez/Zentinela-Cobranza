using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Servicio de aplicación para carteras.
/// Encapsula la regla de negocio: quién tiene acceso a qué carteras.
/// </summary>
public interface ICarteraService
{
    /// <summary>
    /// Retorna las carteras accesibles para el usuario según su rol:
    /// Admin → todas; Líder → solo las asignadas; otros → acceso denegado.
    /// </summary>
    Task<IEnumerable<CarteraDto>> ObtenerCarterasAsync(ContextoUsuario contexto);
}

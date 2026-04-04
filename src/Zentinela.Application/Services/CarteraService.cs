using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Interfaces;

namespace Zentinela.Application.Services;

/// <summary>
/// Implementa la regla de negocio de acceso a carteras.
/// La decisión de quién ve qué vive aquí, no en el controller.
/// </summary>
public class CarteraService : ICarteraService
{
    private readonly ICarteraRepository _carteraRepository;

    public CarteraService(ICarteraRepository carteraRepository)
    {
        _carteraRepository = carteraRepository;
    }

    public async Task<IEnumerable<CarteraDto>> ObtenerCarterasAsync(ContextoUsuario contexto)
    {
        var carteras = contexto.Rol == Roles.Admin
            ? await _carteraRepository.ObtenerTodasAsync()
            : contexto.LiderID.HasValue
                ? await _carteraRepository.ObtenerPorLiderAsync(contexto.LiderID.Value)
                : [];

        return carteras.Select(c => new CarteraDto
        {
            ID = c.ID,
            NombreCartera = c.NombreCartera,
        });
    }
}

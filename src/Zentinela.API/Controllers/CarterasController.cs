using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Core.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CarterasController : ControllerBase
{
    private readonly ICarteraRepository _carteraRepository;

    public CarterasController(ICarteraRepository carteraRepository)
    {
        _carteraRepository = carteraRepository;
    }

    /// <summary>
    /// Obtiene las carteras del líder autenticado.
    /// Admin ve todas; Líder solo ve las suyas.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerCarteras()
    {
        var rol = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var liderIDClaim = User.FindFirst("liderID")?.Value;

        IEnumerable<Zentinela.Core.Entities.Cartera> carteras;

        // Admin ve todas las carteras
        if (rol == "Admin")
        {
            carteras = await _carteraRepository.ObtenerTodasAsync();
        }
        else if (int.TryParse(liderIDClaim, out var liderID))
        {
            carteras = await _carteraRepository.ObtenerPorLiderAsync(liderID);
        }
        else
        {
            return Forbid();
        }

        var resultado = carteras.Select(c => new CarteraDto
        {
            ID = c.ID,
            NombreCartera = c.NombreCartera
        });

        return Ok(resultado);
    }
}

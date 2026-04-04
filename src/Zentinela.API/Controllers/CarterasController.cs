using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CarterasController : ControllerBase
{
    private readonly ICarteraService _carteraService;

    public CarterasController(ICarteraService carteraService)
    {
        _carteraService = carteraService;
    }

    /// <summary>
    /// Obtiene las carteras accesibles para el usuario autenticado.
    /// La regla de negocio (quién ve qué) vive en CarteraService, no aquí.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerCarteras()
    {
        var contexto = new ContextoUsuario(
            Rol: User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null
        );

        var carteras = await _carteraService.ObtenerCarterasAsync(contexto);
        return Ok(carteras);
    }
}

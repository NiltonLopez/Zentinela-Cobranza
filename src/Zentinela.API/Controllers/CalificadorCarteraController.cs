using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/calificador")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
public class CalificadorCarteraController : ControllerBase
{
    private readonly ICalificadorCarteraService _service;

    public CalificadorCarteraController(ICalificadorCarteraService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lista de combinaciones Cartera+Líder disponibles según el rol del usuario.
    /// </summary>
    [HttpGet("opciones")]
    public async Task<IActionResult> ObtenerOpciones()
    {
        var contexto = ObtenerContexto();
        var opciones = await _service.ObtenerOpcionesAsync(contexto);
        return Ok(opciones);
    }

    /// <summary>
    /// Calcula todas las métricas para la asignación Cartera+Líder indicada.
    /// </summary>
    [HttpGet("{carteraID:int}/{liderID:int}")]
    public async Task<IActionResult> Calificar(int carteraID, int liderID)
    {
        var resultado = await _service.CalificarAsync(carteraID, liderID);
        return Ok(resultado);
    }

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);
}

using Microsoft.AspNetCore.Mvc;
using Zentinela.Application.DTOs.Registro;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Endpoint público: cualquier empleado puede auto-registrarse
/// con su cédula y usuario de ControlNext.
/// La validación contra RRHH actúa como control de acceso.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RegistroController : ControllerBase
{
    private readonly IRegistroService _registroService;

    public RegistroController(IRegistroService registroService)
    {
        _registroService = registroService;
    }

    /// <summary>
    /// Registra un nuevo usuario en Zentinela.
    /// Valida la cédula contra RRHH y asigna el rol según el cargo del empleado.
    /// La contraseña inicial es la cédula; se obliga cambio en el primer login.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistroRequest request)
    {
        try
        {
            var resultado = await _registroService.RegistrarAsync(request);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

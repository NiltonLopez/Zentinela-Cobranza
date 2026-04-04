using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application.DTOs.Auth;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Autenticar usuario y obtener token JWT.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await _authService.LoginAsync(request);
        if (resultado is null)
            return Unauthorized(new { mensaje = "Credenciales incorrectas" });

        return Ok(resultado);
    }

    /// <summary>
    /// Retorna los datos del usuario autenticado extraídos del token.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var response = new MeResponse
        {
            Email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            NombreCompleto = User.FindFirstValue("nombreCompleto") ?? string.Empty,
            Rol = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            LiderID = int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null,
        };

        return Ok(response);
    }

    /// <summary>
    /// Cambia la contraseña del usuario autenticado y desactiva el flag de cambio obligatorio.
    /// </summary>
    [HttpPost("cambiar-password")]
    [Authorize]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest request)
    {
        var usuarioIDClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(usuarioIDClaim, out var usuarioID))
            return Unauthorized();

        var errores = await _authService.CambiarPasswordAsync(usuarioID, request);
        if (errores.Any())
            return BadRequest(new { errores });

        return Ok(new { mensaje = "Contraseña actualizada correctamente." });
    }
}

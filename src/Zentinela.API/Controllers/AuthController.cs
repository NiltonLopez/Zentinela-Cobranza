using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Application.DTOs.Auth;
using Zentinela.API.Application.Interfaces;

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
    /// Autenticar usuario y obtener token JWT
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
    /// Obtener datos del usuario autenticado desde el token
    /// </summary>
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
        return Ok(new
        {
            email = User.Identity?.Name,
            rol = claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Role),
            liderID = claims.GetValueOrDefault("liderID"),
            nombreCompleto = claims.GetValueOrDefault("nombreCompleto")
        });
    }
}

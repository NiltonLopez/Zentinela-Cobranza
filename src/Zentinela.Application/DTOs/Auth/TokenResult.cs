namespace Zentinela.Application.DTOs.Auth;

/// <summary>
/// Resultado de la generación de un JWT: incluye el token y su fecha de expiración.
/// Evita que AuthService necesite conocer JwtSettings solo para calcular la expiración.
/// </summary>
public record TokenResult(string Token, DateTime Expiracion);

using Zentinela.Application.DTOs.Auth;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Abstracción para la generación de tokens JWT.
/// Separada de IAuthService para respetar SRP:
/// autenticación y generación de tokens son responsabilidades distintas.
/// Retorna TokenResult con el token y su expiración — AuthService
/// no necesita conocer JwtSettings directamente.
/// </summary>
public interface IJwtTokenGenerator
{
    TokenResult GenerarToken(TokenPayload payload);
}

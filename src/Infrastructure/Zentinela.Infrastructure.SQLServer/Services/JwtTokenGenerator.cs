using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zentinela.Application.DTOs.Auth;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Implementación concreta de generación de JWT.
/// Separada de AuthService para respetar SRP.
/// </summary>
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;

    public JwtTokenGenerator(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public TokenResult GenerarToken(TokenPayload payload)
    {
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var expiracion = DateTime.UtcNow.AddHours(_settings.ExpirationHours);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, payload.UsuarioID.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, payload.Email),
            new Claim(ClaimTypes.Role, payload.Rol),
            new Claim("liderID", payload.LiderID?.ToString() ?? string.Empty),
            new Claim("nombreCompleto", payload.NombreCompleto),
            new Claim("numeroDocumento", payload.NumeroDocumento.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiracion,
            signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256)
        );

        return new TokenResult(
            Token: new JwtSecurityTokenHandler().WriteToken(token),
            Expiracion: expiracion
        );
    }
}

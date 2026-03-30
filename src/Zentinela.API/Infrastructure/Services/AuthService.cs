using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Zentinela.API.Application.DTOs.Auth;
using Zentinela.API.Application.Interfaces;
using Zentinela.API.Domain.Entities;

namespace Zentinela.API.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public AuthService(UserManager<ApplicationUser> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null || !await _userManager.CheckPasswordAsync(usuario, request.Password))
            return null;

        var roles = await _userManager.GetRolesAsync(usuario);
        var rol = roles.FirstOrDefault() ?? "Sin rol";

        var token = GenerarToken(usuario, rol);

        return new LoginResponse
        {
            Token = token,
            NombreCompleto = usuario.NombreCompleto,
            Rol = rol,
            LiderID = usuario.LiderID,
            Expiracion = DateTime.UtcNow.AddHours(
                _configuration.GetValue<int>("JwtSettings:ExpirationHours"))
        };
    }

    private string GenerarToken(ApplicationUser usuario, string rol)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var clave = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email!),
            new Claim(ClaimTypes.Role, rol),
            new Claim("liderID", usuario.LiderID?.ToString() ?? ""),
            new Claim("nombreCompleto", usuario.NombreCompleto),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(
                _configuration.GetValue<int>("JwtSettings:ExpirationHours")),
            signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using Microsoft.AspNetCore.Identity;
using Zentinela.Application.DTOs.Auth;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Models;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Servicio de autenticación: valida credenciales contra Identity y delega
/// la generación del token a IJwtTokenGenerator (SRP).
/// Vive en Infrastructure porque depende de UserManager (ASP.NET Identity).
/// </summary>
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IJwtTokenGenerator tokenGenerator)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var usuario = await _userManager.FindByNameAsync(request.Usuario);

        if (usuario is null || !await _userManager.CheckPasswordAsync(usuario, request.Password))
            return null;

        var roles = await _userManager.GetRolesAsync(usuario);
        var rol = roles.FirstOrDefault() ?? string.Empty;

        var payload = new TokenPayload(
            UsuarioID: usuario.Id,
            Email: usuario.Email!,
            NombreCompleto: usuario.NombreCompleto,
            Rol: rol,
            LiderID: usuario.LiderID,
            NumeroDocumento: usuario.NumeroDocumento
        );

        var tokenResult = _tokenGenerator.GenerarToken(payload);

        return new LoginResponse
        {
            Token = tokenResult.Token,
            Email = usuario.Email!,
            NombreCompleto = usuario.NombreCompleto,
            Rol = rol,
            LiderID = usuario.LiderID,
            Expiracion = tokenResult.Expiracion,
            RequiereCambioPassword = usuario.RequiereCambioPassword,
        };
    }

    public async Task<IEnumerable<string>> CambiarPasswordAsync(int usuarioID, CambiarPasswordRequest request)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString());
        if (usuario is null)
            return ["Usuario no encontrado."];

        var resultado = await _userManager.ChangePasswordAsync(
            usuario, request.PasswordActual, request.NuevoPassword);

        if (!resultado.Succeeded)
            return resultado.Errors.Select(e => e.Description);

        // Desactiva el flag de cambio obligatorio
        usuario.RequiereCambioPassword = false;
        await _userManager.UpdateAsync(usuario);

        return [];
    }
}

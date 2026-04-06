using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Admin;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Models;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Operaciones administrativas sobre usuarios de Zentinela.
/// Vive en Infrastructure porque depende directamente de UserManager (ASP.NET Identity).
/// </summary>
public class AdminService : IAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IEnumerable<UsuarioAdminDto>> ListarUsuariosAsync()
    {
        var usuarios = await _userManager.Users
            .OrderBy(u => u.NombreCompleto)
            .ToListAsync();

        var resultado = new List<UsuarioAdminDto>();

        foreach (var u in usuarios)
        {
            var roles = await _userManager.GetRolesAsync(u);
            resultado.Add(new UsuarioAdminDto
            {
                Id = u.Id,
                NombreCompleto = u.NombreCompleto,
                Usuario = u.UserName ?? string.Empty,
                NumeroDocumento = u.NumeroDocumento,
                Rol = roles.FirstOrDefault() ?? string.Empty,
                RequiereCambioPassword = u.RequiereCambioPassword,
            });
        }

        return resultado;
    }

    public async Task ResetPasswordAsync(int usuarioID)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        // Resetea la contraseña al número de documento
        await _userManager.RemovePasswordAsync(usuario);
        var resultado = await _userManager.AddPasswordAsync(
            usuario, usuario.NumeroDocumento.ToString());

        if (!resultado.Succeeded)
        {
            var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo resetear la contraseña: {errores}");
        }

        // Obliga al usuario a cambiar su contraseña en el próximo login
        usuario.RequiereCambioPassword = true;
        await _userManager.UpdateAsync(usuario);
    }
}

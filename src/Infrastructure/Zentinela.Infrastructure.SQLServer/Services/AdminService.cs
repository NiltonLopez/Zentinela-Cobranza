using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application.DTOs.Admin;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Operaciones administrativas sobre usuarios de Zentinela.
/// Vive en Infrastructure porque depende directamente de UserManager (ASP.NET Identity).
/// </summary>
public class AdminService : IAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly CallCenterDbContext _callCenter;

    public AdminService(
        UserManager<ApplicationUser> userManager,
        RecursosHumanosDbContext rrhh,
        CallCenterDbContext callCenter)
    {
        _userManager = userManager;
        _rrhh = rrhh;
        _callCenter = callCenter;
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

    public async Task<IEnumerable<SupervisorDto>> ListarSupervisoresAsync()
    {
        var cargosSupervisores = new[] { 2, 7, 8, 13, 18 };

        return await _rrhh.Empleados
            .Include(e => e.Cargo)
            .Where(e => cargosSupervisores.Contains(e.CargoID) && e.EstadoEmpleado)
            .OrderBy(e => e.Nombre)
            .Select(e => new SupervisorDto
            {
                Id              = e.ID,
                NombreCompleto  = e.Nombre + " " + e.Apellidos,
                Cargo           = e.Cargo.Descripcion,
                NumeroDocumento = e.NumeroDocumento,
            })
            .ToListAsync();
    }

    public async Task ReasignarSupervisorAsync(int usuarioID, int nuevoSupervisorEmpleadoId)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        var empleado = await _rrhh.Empleados
            .FirstOrDefaultAsync(e => e.NumeroDocumento == usuario.NumeroDocumento)
            ?? throw new InvalidOperationException("Empleado no encontrado en DBRecursosHumanos.");

        // Verificar que el nuevo supervisor exista
        var nuevoSupervisorExiste = await _rrhh.Empleados
            .AnyAsync(e => e.ID == nuevoSupervisorEmpleadoId);
        if (!nuevoSupervisorExiste)
            throw new InvalidOperationException("El supervisor seleccionado no existe.");

        // Actualizar supervisor en RRHH
        empleado.IDSupervisor = nuevoSupervisorEmpleadoId;
        await _rrhh.SaveChangesAsync();

        // Limpiar todas las asignaciones de cartera del agente
        var asignaciones = _callCenter.CarteraAgentes
            .Where(ca => ca.AgenteID == empleado.NumeroDocumento);
        _callCenter.CarteraAgentes.RemoveRange(asignaciones);
        await _callCenter.SaveChangesAsync();
    }

    public async Task<SupervisorDto?> ObtenerSupervisorActualAsync(int usuarioID)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString());
        if (usuario == null) return null;

        var empleado = await _rrhh.Empleados
            .FirstOrDefaultAsync(e => e.NumeroDocumento == usuario.NumeroDocumento);
        if (empleado?.IDSupervisor == null) return null;

        return await _rrhh.Empleados
            .Include(e => e.Cargo)
            .Where(e => e.ID == empleado.IDSupervisor)
            .Select(e => new SupervisorDto
            {
                Id              = e.ID,
                NombreCompleto  = e.Nombre + " " + e.Apellidos,
                Cargo           = e.Cargo.Descripcion,
                NumeroDocumento = e.NumeroDocumento,
            })
            .FirstOrDefaultAsync();
    }

    public async Task InactivarUsuarioAsync(int usuarioID)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        // Poner EstadoEmpleado = false en DBRecursosHumanos
        var empleado = await _rrhh.Empleados
            .FirstOrDefaultAsync(e => e.NumeroDocumento == usuario.NumeroDocumento);

        if (empleado != null)
        {
            empleado.EstadoEmpleado = false;
            await _rrhh.SaveChangesAsync();
        }

        // Eliminar el usuario de Zentinela
        var resultado = await _userManager.DeleteAsync(usuario);
        if (!resultado.Succeeded)
        {
            var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo eliminar el usuario: {errores}");
        }
    }
}

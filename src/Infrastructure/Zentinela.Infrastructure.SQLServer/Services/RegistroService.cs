using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Registro;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Models;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.Infrastructure.SQLServer.Services;

/// <summary>
/// Orquesta el registro de un nuevo usuario:
/// 1. Valida cédula en DBRecursosHumanos.
/// 2. Determina el rol por CargoID.
/// 3. Crea Agente en DBCallCenter.
/// 4. Crea usuario Identity en DBZentinela.
/// Vive en Infrastructure porque depende de 3 DbContexts y UserManager.
/// </summary>
public class RegistroService : IRegistroService
{
    private readonly RecursosHumanosDbContext _rrhh;
    private readonly CallCenterDbContext _callCenter;
    private readonly UserManager<ApplicationUser> _userManager;

    public RegistroService(
        RecursosHumanosDbContext rrhh,
        CallCenterDbContext callCenter,
        UserManager<ApplicationUser> userManager)
    {
        _rrhh = rrhh;
        _callCenter = callCenter;
        _userManager = userManager;
    }

    public async Task<RegistroResponse> RegistrarAsync(RegistroRequest request)
    {
        // 1. Validar cédula contra DBRecursosHumanos
        var empleado = await _rrhh.Empleados
            .Include(e => e.Cargo)
            .FirstOrDefaultAsync(e => e.NumeroDocumento == request.Cedula);

        if (empleado is null)
            throw new InvalidOperationException(
                "No se encuentra en la base de datos de la empresa, comuníquese con RRHH.");

        if (!empleado.EstadoEmpleado)
            throw new InvalidOperationException(
                "El empleado se encuentra inactivo. Comuníquese con RRHH.");

        // 2. Determinar rol por CargoID
        var rol = CargoRoles.ObtenerRol(empleado.CargoID);
        if (rol is null)
            throw new InvalidOperationException(
                $"El cargo '{empleado.Cargo.Descripcion}' no tiene acceso al sistema. " +
                "Comuníquese con el administrador.");

        // 3. Verificar que la cédula no esté ya registrada en Zentinela
        var usuarioExistente = await _userManager.Users
            .AnyAsync(u => u.NumeroDocumento == request.Cedula);
        if (usuarioExistente)
            throw new InvalidOperationException("Esta cédula ya tiene una cuenta registrada.");

        // 4. Verificar que el UsuarioControlNext no esté ya en uso
        var usernameTomado = await _userManager.FindByNameAsync(request.UsuarioControlNext);
        if (usernameTomado is not null)
            throw new InvalidOperationException(
                $"El usuario '{request.UsuarioControlNext}' ya está registrado.");

        // 5. Crear Agente en DBCallCenter
        var agenteExiste = await _callCenter.Agentes
            .AnyAsync(a => a.AgenteID == request.Cedula);

        if (!agenteExiste)
        {
            _callCenter.Agentes.Add(new Agente
            {
                AgenteID = request.Cedula,
                UsuarioControlNext = request.UsuarioControlNext,
                Activo = true,
            });
            await _callCenter.SaveChangesAsync();
        }

        // 6. Crear usuario Identity en DBZentinela
        var nombreCompleto = $"{empleado.Nombre} {empleado.Apellidos}".Trim();
        var nuevoUsuario = new ApplicationUser
        {
            UserName = request.UsuarioControlNext,
            Email = empleado.Email ?? $"{request.UsuarioControlNext}@zentinela.local",
            EmailConfirmed = true,
            NombreCompleto = nombreCompleto,
            NumeroDocumento = request.Cedula,
            RequiereCambioPassword = true,
            LiderID = null, // Se asigna desde Gestión de Personal si aplica
        };

        // Contraseña inicial = cédula como string (el usuario la cambia en el primer login)
        var resultado = await _userManager.CreateAsync(nuevoUsuario, request.Cedula.ToString());
        if (!resultado.Succeeded)
        {
            // Revertir el Agente creado si Identity falla
            var agenteCreado = await _callCenter.Agentes.FindAsync(request.Cedula);
            if (agenteCreado is not null)
            {
                _callCenter.Agentes.Remove(agenteCreado);
                await _callCenter.SaveChangesAsync();
            }

            var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo crear el usuario: {errores}");
        }

        await _userManager.AddToRoleAsync(nuevoUsuario, rol);

        return new RegistroResponse
        {
            NombreCompleto = nombreCompleto,
            Rol = rol,
            Mensaje = $"Usuario registrado exitosamente con el rol '{rol}'.",
        };
    }
}

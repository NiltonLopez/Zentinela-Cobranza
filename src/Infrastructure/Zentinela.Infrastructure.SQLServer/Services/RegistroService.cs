using System.Globalization;
using System.Text;
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

        // 4. Calcular UsuarioControlNext: primerNombre.primerApellido + últimos 3 dígitos del documento
        // Identity rechaza ñ y tildes en el username, así que el slug se hace ASCII puro.
        // El nombre real (con ñ/tildes) se conserva intacto en NombreCompleto.
        var primerNombre  = empleado.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? empleado.Nombre;
        var primerApellido = empleado.Apellidos.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? empleado.Apellidos;
        var ultimos3 = request.Cedula.ToString()[^Math.Min(3, request.Cedula.ToString().Length)..];
        var usuarioControlNext = SlugAscii($"{primerNombre}.{primerApellido}{ultimos3}");

        var usernameTomado = await _userManager.FindByNameAsync(usuarioControlNext);
        if (usernameTomado is not null)
            throw new InvalidOperationException(
                $"El usuario '{usuarioControlNext}' ya está registrado. Comuníquese con el administrador.");

        // 5. Crear Agente en DBCallCenter
        var agenteExiste = await _callCenter.Agentes
            .AnyAsync(a => a.AgenteID == request.Cedula);

        if (!agenteExiste)
        {
            _callCenter.Agentes.Add(new Agente
            {
                AgenteID = request.Cedula,
                UsuarioControlNext = usuarioControlNext,
                Activo = true,
            });
            await _callCenter.SaveChangesAsync();
        }

        // 6. Resolver LiderID si el rol es Líder
        int? liderID = null;
        if (rol == Roles.Lider)
        {
            var lider = await _callCenter.Lideres
                .FirstOrDefaultAsync(l => l.NumeroDocumento == request.Cedula && l.Activo);
            liderID = lider?.ID;
        }

        // 7. Crear usuario Identity en DBZentinela
        var nombreCompleto = $"{empleado.Nombre} {empleado.Apellidos}".Trim();
        var nuevoUsuario = new ApplicationUser
        {
            UserName = usuarioControlNext,
            Email = empleado.Email ?? $"{usuarioControlNext}@zentinela.local",
            EmailConfirmed = true,
            NombreCompleto = nombreCompleto,
            NumeroDocumento = request.Cedula,
            RequiereCambioPassword = true,
            LiderID = liderID,
        };

        // 8. Crear usuario con contraseña inicial = cédula (se cambia en el primer login)
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
            Usuario = usuarioControlNext,
            Rol = rol,
            Mensaje = $"Usuario registrado exitosamente con el rol '{rol}'.",
        };
    }

    /// <summary>
    /// Convierte un texto a slug ASCII apto para username de Identity:
    /// quita diacríticos (ñ→n, á→a, é→e...), pasa a minúsculas y descarta
    /// cualquier carácter fuera del set permitido por defecto en Identity.
    /// </summary>
    private static string SlugAscii(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // descarta tildes/diéresis combinantes
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-' || c == '@' || c == '+')
                sb.Append(c);
            // resto: caracteres no soportados por Identity → se descartan silenciosamente
        }
        return sb.ToString().ToLowerInvariant();
    }
}

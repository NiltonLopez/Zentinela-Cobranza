using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zentinela.Application;
using Zentinela.Application.DTOs.Admin;
using Zentinela.Application.Interfaces;
using Zentinela.Core.Entities;
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

        // ── Batch: supervisor por usuario — una sola query usando navegación ──
        var docs = usuarios.Select(u => u.NumeroDocumento).ToList();

        // Aprovecha la relación recursiva Empleado.Supervisor ya mapeada.
        // Resultado: NumeroDocumento → nombre completo del supervisor (o null)
        var supervisorMap = await _rrhh.Empleados
            .Where(e => docs.Contains(e.NumeroDocumento))
            .Select(e => new
            {
                e.NumeroDocumento,
                NombreSupervisor = e.Supervisor != null
                    ? e.Supervisor.Nombre + " " + e.Supervisor.Apellidos
                    : (string?)null,
            })
            .ToDictionaryAsync(e => e.NumeroDocumento, e => e.NombreSupervisor);

        // ── Roles (limitación de UserManager: una llamada por usuario) ────────
        var resultado = new List<UsuarioAdminDto>();
        foreach (var u in usuarios)
        {
            var roles = await _userManager.GetRolesAsync(u);
            supervisorMap.TryGetValue(u.NumeroDocumento, out var supervisorNombre);

            resultado.Add(new UsuarioAdminDto
            {
                Id                     = u.Id,
                NombreCompleto         = u.NombreCompleto,
                Usuario                = u.UserName ?? string.Empty,
                NumeroDocumento        = u.NumeroDocumento,
                Rol                    = roles.FirstOrDefault() ?? string.Empty,
                RequiereCambioPassword = u.RequiereCambioPassword,
                NombreSupervisor       = supervisorNombre,
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

    public async Task<IEnumerable<SupervisorDto>> ListarSupervisoresActivosAsync()
    {
        // Obtiene los IDs únicos que aparecen como supervisor de algún empleado
        var supervisorIds = await _rrhh.Empleados
            .Where(e => e.IDSupervisor != null)
            .Select(e => e.IDSupervisor!.Value)
            .Distinct()
            .ToListAsync();

        return await _rrhh.Empleados
            .Include(e => e.Cargo)
            .Where(e => supervisorIds.Contains(e.ID))
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

        // El puente entre Zentinela y RRHH es NumeroDocumento (cédula).
        // ApplicationUser.NumeroDocumento == Empleado.NumeroDocumento (long).
        var empleado = await _rrhh.Empleados
            .FirstOrDefaultAsync(e => e.NumeroDocumento == usuario.NumeroDocumento)
            ?? throw new InvalidOperationException("Empleado no encontrado en DBRecursosHumanos.");

        // Verificar que el nuevo supervisor exista
        var nuevoSupervisorExiste = await _rrhh.Empleados
            .AnyAsync(e => e.ID == nuevoSupervisorEmpleadoId);
        if (!nuevoSupervisorExiste)
            throw new InvalidOperationException("El supervisor seleccionado no existe.");

        // Actualizar campo recursivo IDSupervisor en RRHH (FK a Empleado.ID del mismo servicio)
        empleado.IDSupervisor = nuevoSupervisorEmpleadoId;
        await _rrhh.SaveChangesAsync();

        // Limpiar todas las asignaciones de cartera del agente en DBCallCenter.
        // Al cambiar de supervisor las carteras quedan desactualizadas; el nuevo supervisor
        // deberá reasignarlas. Se usa el NumeroDocumento como AgenteID (convención del sistema).
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

    /// <summary>
    /// Reemplaza el rol actual del usuario por <paramref name="nuevoRol"/>.
    /// Si el nuevo rol es Líder, resuelve el LiderID en DBCallCenter por NumeroDocumento;
    /// en cualquier otro caso limpia LiderID para evitar permisos residuales.
    /// </summary>
    public async Task CambiarRolAsync(int usuarioID, string nuevoRol)
    {
        // Validar que el rol solicitado exista en el sistema
        var rolesValidos = new[]
        {
            Roles.Admin, Roles.Lider, Roles.Director, Roles.Asesor,
            Roles.LiderExterno, Roles.AsesorExterno,
        };
        if (!rolesValidos.Contains(nuevoRol))
            throw new InvalidOperationException(
                $"El rol '{nuevoRol}' no es válido. Roles disponibles: {string.Join(", ", rolesValidos)}.");

        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        // Remover todos los roles actuales (un usuario tiene exactamente un rol)
        var rolesActuales = await _userManager.GetRolesAsync(usuario);
        if (rolesActuales.Any())
            await _userManager.RemoveFromRolesAsync(usuario, rolesActuales);

        // Líder normal y Líder Externo necesitan LiderID resuelto desde DBCallCenter
        // para que los módulos operativos (Calificador, Creador, Comparador) filtren
        // por sus carteras propias.
        if (nuevoRol == Roles.Lider || nuevoRol == Roles.LiderExterno)
        {
            var lider = await _callCenter.Lideres
                .FirstOrDefaultAsync(l => l.NumeroDocumento == usuario.NumeroDocumento && l.Activo);
            usuario.LiderID = lider?.ID;
        }
        else
        {
            // Cualquier otro rol no necesita LiderID
            usuario.LiderID = null;
        }

        await _userManager.UpdateAsync(usuario);
        await _userManager.AddToRoleAsync(usuario, nuevoRol);
    }

    public async Task InactivarUsuarioAsync(int usuarioID)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        // Marcar inactivo en RRHH solo si el empleado existe en esa BD.
        // Es posible que un usuario de Zentinela no tenga registro en RRHH
        // (p.ej. usuarios creados manualmente), en ese caso se omite este paso.
        var empleado = await _rrhh.Empleados
            .FirstOrDefaultAsync(e => e.NumeroDocumento == usuario.NumeroDocumento);

        if (empleado != null)
        {
            empleado.EstadoEmpleado = false;
            await _rrhh.SaveChangesAsync();
        }

        // Eliminar el ApplicationUser de DBZentinela (Identity).
        // Cascada de Identity elimina también AspNetUserRoles y AspNetUserClaims.
        var resultado = await _userManager.DeleteAsync(usuario);
        if (!resultado.Succeeded)
        {
            var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo eliminar el usuario: {errores}");
        }
    }

    // ── Gestión de Agentes (Cartera_Agente) ──────────────────────────────────

    private const string MotivoSinSupervisor   = "Sin supervisor asignado";
    private const string MotivoSupervisorNoLider = "Supervisor no es líder activo";

    public async Task<IEnumerable<AgenteAdminDto>> ListarAgentesAsync()
    {
        // 1. Usuarios con perfil "Asesor" en Zentinela (Identity).
        //    Es lo que el negocio llama "Agente". Tener ApplicationUser sin este rol no basta.
        var asesores = await _userManager.GetUsersInRoleAsync(Roles.Asesor);
        if (asesores.Count == 0) return [];
        var docsAsesores = asesores.Select(u => u.NumeroDocumento).ToHashSet();

        // 2. Agentes activos en DBCallCenter cuyo AgenteID está entre los asesores con rol.
        var agenteIDs = await _callCenter.Agentes
            .Where(a => a.Activo)
            .Select(a => a.AgenteID)
            .Where(id => docsAsesores.Contains(id))
            .ToListAsync();
        if (agenteIDs.Count == 0) return [];

        // 3. Empleados activos en RRHH cuyo NumeroDocumento coincide con AgenteID.
        //    Include(Supervisor) para resolver supervisor en una sola query.
        var empleados = await _rrhh.Empleados
            .Include(e => e.Supervisor)
            .Where(e => agenteIDs.Contains(e.NumeroDocumento) && e.EstadoEmpleado)
            .ToListAsync();
        if (empleados.Count == 0) return [];

        // 4. Líderes activos en DBCallCenter cuya cédula coincide con la del supervisor.
        var supervisorDocs = empleados
            .Where(e => e.Supervisor != null)
            .Select(e => e.Supervisor!.NumeroDocumento)
            .Distinct()
            .ToList();

        var lideres = await _callCenter.Lideres
            .Where(l => l.Activo && l.NumeroDocumento != null && supervisorDocs.Contains(l.NumeroDocumento.Value))
            .ToListAsync();

        var liderPorDoc = lideres.ToDictionary(l => l.NumeroDocumento!.Value);

        // 5. Conteos en Cartera_Agente para los pares (AgenteID, LiderID) relevantes.
        //    Solo contamos por LiderID actual del agente (filas con LiderID antiguo se ignoran).
        var carteraCounts = await _callCenter.CarteraAgentes
            .Where(ca => agenteIDs.Contains(ca.AgenteID))
            .GroupBy(ca => new { ca.AgenteID, ca.LiderID })
            .Select(g => new { g.Key.AgenteID, g.Key.LiderID, Count = g.Count() })
            .ToListAsync();

        // 6. Componer resultado.
        var resultado = new List<AgenteAdminDto>(empleados.Count);
        foreach (var e in empleados)
        {
            var supervisor = e.Supervisor;
            Lider? lider = null;
            string? motivo = null;

            if (supervisor == null)
                motivo = MotivoSinSupervisor;
            else if (!liderPorDoc.TryGetValue(supervisor.NumeroDocumento, out lider))
                motivo = MotivoSupervisorNoLider;

            var totalCarteras = lider == null
                ? 0
                : carteraCounts.FirstOrDefault(c => c.AgenteID == e.NumeroDocumento && c.LiderID == lider.ID)?.Count ?? 0;

            resultado.Add(new AgenteAdminDto
            {
                AgenteID               = e.NumeroDocumento,
                NombreCompleto         = (e.Nombre + " " + e.Apellidos).Trim(),
                NumeroDocumento        = e.NumeroDocumento,
                SupervisorNombre       = supervisor != null ? (supervisor.Nombre + " " + supervisor.Apellidos).Trim() : null,
                EsLiderSupervisor      = lider != null,
                LiderID                = lider?.ID,
                MotivoNoAsignable      = motivo,
                TotalCarterasAsignadas = totalCarteras,
            });
        }

        return resultado.OrderBy(r => r.NombreCompleto, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-CO"), ignoreCase: true));
    }

    public async Task<CarterasDelAgenteDto> ObtenerCarterasDelAgenteAsync(long agenteID)
    {
        var (lider, motivo) = await ResolverLiderDelAgenteAsync(agenteID);

        if (lider == null)
        {
            return new CarterasDelAgenteDto
            {
                MotivoNoAsignable = motivo,
                Carteras          = [],
            };
        }

        // Carteras del líder + flag de asignación al agente. Solo relaciones activas:
        // no se ofrece asignar asesores a una relación retirada (ADR-022).
        var carterasLider = await _callCenter.LiderCarteras
            .Where(lc => lc.LiderID == lider.ID && lc.Activo)
            .Select(lc => new { lc.CarteraID, Nombre = lc.Cartera.NombreCartera })
            .ToListAsync();

        var asignadas = await _callCenter.CarteraAgentes
            .Where(ca => ca.AgenteID == agenteID && ca.LiderID == lider.ID)
            .Select(ca => ca.CarteraID)
            .ToHashSetAsync();

        var items = carterasLider
            .Select(c => new CarteraAgenteItemDto
            {
                CarteraID = c.CarteraID,
                Nombre    = c.Nombre,
                Asignada  = asignadas.Contains(c.CarteraID),
            })
            .OrderBy(c => c.Nombre, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-CO"), ignoreCase: true))
            .ToList();

        return new CarterasDelAgenteDto
        {
            LiderID     = lider.ID,
            LiderNombre = lider.Nombre,
            Carteras    = items,
        };
    }

    public async Task AsignarCarteraAgenteAsync(long agenteID, int carteraID)
    {
        var (lider, motivo) = await ResolverLiderDelAgenteAsync(agenteID);
        if (lider == null)
            throw new InvalidOperationException(motivo ?? "No se puede asignar.");

        // Idempotente: si ya existe la fila, no hacer nada.
        var yaExiste = await _callCenter.CarteraAgentes
            .AnyAsync(ca => ca.AgenteID == agenteID && ca.LiderID == lider.ID && ca.CarteraID == carteraID);
        if (yaExiste) return;

        _callCenter.CarteraAgentes.Add(new CarteraAgente
        {
            AgenteID  = agenteID,
            LiderID   = lider.ID,
            CarteraID = carteraID,
        });

        try
        {
            await _callCenter.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // FK compuesta (LiderID, CarteraID) → Lider_Cartera. Si la cartera no
            // pertenece al líder, SQL rechaza la inserción. Mensaje legible para el usuario.
            throw new InvalidOperationException("La cartera no pertenece al líder del agente.");
        }
    }

    public async Task DesasignarCarteraAgenteAsync(long agenteID, int carteraID)
    {
        var (lider, motivo) = await ResolverLiderDelAgenteAsync(agenteID);
        if (lider == null)
            throw new InvalidOperationException(motivo ?? "No se puede desasignar.");

        var fila = await _callCenter.CarteraAgentes
            .FirstOrDefaultAsync(ca => ca.AgenteID == agenteID && ca.LiderID == lider.ID && ca.CarteraID == carteraID);
        if (fila == null) return; // idempotente

        _callCenter.CarteraAgentes.Remove(fila);
        await _callCenter.SaveChangesAsync();
    }

    /// <summary>
    /// Resuelve el Líder activo asociado al supervisor del agente:
    ///   AgenteID → Empleado.NumeroDocumento → Empleado.Supervisor.NumeroDocumento → Lider.NumeroDocumento.
    /// Devuelve (null, motivo) si falla en cualquier paso.
    /// </summary>
    private async Task<(Lider? lider, string? motivo)> ResolverLiderDelAgenteAsync(long agenteID)
    {
        var empleado = await _rrhh.Empleados
            .Include(e => e.Supervisor)
            .FirstOrDefaultAsync(e => e.NumeroDocumento == agenteID && e.EstadoEmpleado)
            ?? throw new InvalidOperationException("Agente no encontrado o inactivo.");

        if (empleado.Supervisor == null)
            return (null, MotivoSinSupervisor);

        var supDoc = empleado.Supervisor.NumeroDocumento;
        var lider = await _callCenter.Lideres
            .FirstOrDefaultAsync(l => l.Activo && l.NumeroDocumento == supDoc);

        if (lider == null)
            return (null, MotivoSupervisorNoLider);

        return (lider, null);
    }

    public async Task CambiarUsuarioAsync(int usuarioID, string nuevoUsuario)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioID.ToString())
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        var limpio = (nuevoUsuario ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(limpio))
            throw new InvalidOperationException("El usuario no puede estar vacío.");

        if (limpio.Length < 3 || limpio.Length > 50)
            throw new InvalidOperationException("El usuario debe tener entre 3 y 50 caracteres.");

        // Sin cambios reales — UserManager.SetUserNameAsync con el mismo nombre devuelve éxito,
        // pero evitamos la llamada para no normalizar/tocar la BD innecesariamente.
        if (string.Equals(usuario.UserName, limpio, StringComparison.Ordinal))
            return;

        // Unicidad case-insensitive (Identity normaliza UserName a uppercase para búsquedas).
        var existente = await _userManager.FindByNameAsync(limpio);
        if (existente != null && existente.Id != usuarioID)
            throw new InvalidOperationException($"El usuario '{limpio}' ya está en uso.");

        var resultado = await _userManager.SetUserNameAsync(usuario, limpio);
        if (!resultado.Succeeded)
        {
            var errores = string.Join(", ", resultado.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo cambiar el usuario: {errores}");
        }

        // Mantener Agente.UsuarioControlNext sincronizado con el UserName de Identity.
        // El vínculo es por cédula: Agente.AgenteID == ApplicationUser.NumeroDocumento.
        // Si el usuario no es un agente (p. ej. Admin sin presencia en DBCallCenter), se omite.
        var agente = await _callCenter.Agentes
            .FirstOrDefaultAsync(a => a.AgenteID == usuario.NumeroDocumento);
        if (agente is not null)
        {
            agente.UsuarioControlNext = limpio;
            await _callCenter.SaveChangesAsync();
        }
    }
}

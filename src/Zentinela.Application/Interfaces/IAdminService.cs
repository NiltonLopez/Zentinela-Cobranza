using Zentinela.Application.DTOs.Admin;

namespace Zentinela.Application.Interfaces;

public interface IAdminService
{
    /// <summary>
    /// Retorna todos los usuarios registrados en Zentinela con su rol asignado.
    /// </summary>
    Task<IEnumerable<UsuarioAdminDto>> ListarUsuariosAsync();

    /// <summary>
    /// Resetea la contraseña del usuario al número de documento
    /// y activa el flag RequiereCambioPassword para obligar cambio en el próximo login.
    /// </summary>
    Task ResetPasswordAsync(int usuarioID);

    /// <summary>
    /// Retorna los empleados con cargos que pueden supervisar (CargoID ∈ {2,7,8,13,18}).
    /// Usado en el modal de reasignación.
    /// </summary>
    Task<IEnumerable<SupervisorDto>> ListarSupervisoresAsync();

    /// <summary>
    /// Retorna todos los empleados cuyo ID aparece en el campo IDSupervisor de algún otro
    /// empleado. Fuente de datos para el filtro de supervisores en la tabla de personal.
    /// </summary>
    Task<IEnumerable<SupervisorDto>> ListarSupervisoresActivosAsync();

    /// <summary>
    /// Reasigna el supervisor de un usuario:
    /// actualiza Empleado.IDSupervisor en DBRecursosHumanos y limpia
    /// todas sus asignaciones en Cartera_Agente.
    /// </summary>
    Task ReasignarSupervisorAsync(int usuarioID, int nuevoSupervisorEmpleadoId);

    /// <summary>
    /// Retorna el supervisor actual del usuario (Empleado.IDSupervisor → Empleado).
    /// Devuelve null si no tiene supervisor asignado o si el empleado no existe en RRHH.
    /// </summary>
    Task<SupervisorDto?> ObtenerSupervisorActualAsync(int usuarioID);

    /// <summary>
    /// Inactiva un usuario: pone EstadoEmpleado = false en DBRecursosHumanos
    /// y elimina el ApplicationUser de Zentinela.
    /// </summary>
    Task InactivarUsuarioAsync(int usuarioID);
}

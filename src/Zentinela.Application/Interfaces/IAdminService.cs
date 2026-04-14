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
    /// </summary>
    Task<IEnumerable<SupervisorDto>> ListarSupervisoresAsync();

    /// <summary>
    /// Reasigna el supervisor de un usuario:
    /// actualiza Empleado.IDSupervisor en DBRecursosHumanos y limpia
    /// todas sus asignaciones en Cartera_Agente.
    /// </summary>
    Task ReasignarSupervisorAsync(int usuarioID, int nuevoSupervisorEmpleadoId);
}

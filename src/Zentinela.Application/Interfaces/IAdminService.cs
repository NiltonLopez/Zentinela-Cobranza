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
}

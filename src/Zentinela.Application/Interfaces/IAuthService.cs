using Zentinela.Application.DTOs.Auth;

namespace Zentinela.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>
    /// Cambia la contraseña del usuario autenticado y desactiva el flag RequiereCambioPassword.
    /// Retorna los errores de Identity si el cambio falla, o lista vacía si tiene éxito.
    /// </summary>
    Task<IEnumerable<string>> CambiarPasswordAsync(int usuarioID, CambiarPasswordRequest request);
}

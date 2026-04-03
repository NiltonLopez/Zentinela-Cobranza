using Zentinela.Application.DTOs.Auth;

namespace Zentinela.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}

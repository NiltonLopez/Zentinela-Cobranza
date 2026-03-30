using Zentinela.API.Application.DTOs.Auth;

namespace Zentinela.API.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}

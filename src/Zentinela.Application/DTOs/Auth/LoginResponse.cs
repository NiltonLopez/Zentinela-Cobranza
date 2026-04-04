namespace Zentinela.Application.DTOs.Auth;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public int? LiderID { get; set; }
    public DateTime Expiracion { get; set; }
    public bool RequiereCambioPassword { get; set; }
}

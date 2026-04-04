namespace Zentinela.Application.DTOs.Auth;

/// <summary>
/// DTO tipado para el endpoint GET /api/auth/me.
/// Evita retornar objetos anónimos sin contrato de API.
/// </summary>
public class MeResponse
{
    public string Email { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public int? LiderID { get; set; }
}

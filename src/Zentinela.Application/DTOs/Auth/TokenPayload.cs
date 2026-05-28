namespace Zentinela.Application.DTOs.Auth;

/// <summary>
/// Datos necesarios para generar un JWT.
/// Desacopla IJwtTokenGenerator de cualquier modelo de Identity o entidad de dominio.
/// </summary>
public record TokenPayload(
    int UsuarioID,
    string UserName,
    string Email,
    string NombreCompleto,
    string Rol,
    int? LiderID,
    long NumeroDocumento
);

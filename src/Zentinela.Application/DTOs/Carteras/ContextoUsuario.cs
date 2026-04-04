namespace Zentinela.Application.DTOs.Carteras;

/// <summary>
/// Contexto del usuario autenticado para operaciones de negocio.
/// Evita pasar ClaimsPrincipal (concepto web) a la capa de aplicación.
/// </summary>
public record ContextoUsuario(string Rol, int? LiderID);

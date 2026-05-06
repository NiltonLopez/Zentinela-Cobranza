namespace Zentinela.Application.DTOs.Admin;

public class CambiarUsuarioDto
{
    /// <summary>Nuevo username (ApplicationUser.UserName) para el usuario.</summary>
    public string NuevoUsuario { get; set; } = string.Empty;
}

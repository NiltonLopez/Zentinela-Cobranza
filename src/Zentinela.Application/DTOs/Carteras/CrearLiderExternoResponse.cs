namespace Zentinela.Application.DTOs.Carteras;

/// <summary>
/// Resultado del alta de un líder externo, con las credenciales del usuario Zentinela creado.
/// <see cref="PasswordTemporal"/> solo trae valor cuando el sistema GENERÓ la contraseña
/// (líder sin documento) y debe mostrarse una única vez al administrador. Cuando hay documento,
/// la contraseña inicial ES el documento y PasswordTemporal queda null.
/// En ambos casos el usuario debe cambiarla en el primer inicio de sesión.
/// </summary>
public class CrearLiderExternoResponse
{
    public int     LiderID          { get; set; }
    public string  Nombre           { get; set; } = string.Empty;
    public string  Usuario          { get; set; } = string.Empty;
    public string? PasswordTemporal { get; set; }
}

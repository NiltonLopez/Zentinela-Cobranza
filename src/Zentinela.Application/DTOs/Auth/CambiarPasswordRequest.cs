using System.ComponentModel.DataAnnotations;

namespace Zentinela.Application.DTOs.Auth;

public class CambiarPasswordRequest
{
    [Required]
    public string PasswordActual { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NuevoPassword { get; set; } = string.Empty;
}

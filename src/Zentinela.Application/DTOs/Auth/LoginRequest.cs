using System.ComponentModel.DataAnnotations;

namespace Zentinela.Application.DTOs.Auth;

public class LoginRequest
{
    [Required]
    public string Usuario { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

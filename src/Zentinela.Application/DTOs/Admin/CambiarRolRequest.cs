using System.ComponentModel.DataAnnotations;

namespace Zentinela.Application.DTOs.Admin;

public class CambiarRolRequest
{
    [Required]
    public string Rol { get; set; } = string.Empty;
}

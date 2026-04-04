using System.ComponentModel.DataAnnotations;

namespace Zentinela.Application.DTOs.Registro;

public class RegistroRequest
{
    [Required]
    public long Cedula { get; set; }

    [Required]
    [MaxLength(100)]
    public string UsuarioControlNext { get; set; } = string.Empty;
}

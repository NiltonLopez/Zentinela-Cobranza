namespace Zentinela.Application.DTOs.Admin;

public class UsuarioAdminDto
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public long NumeroDocumento { get; set; }
    public string Rol { get; set; } = string.Empty;
    public bool RequiereCambioPassword { get; set; }
}

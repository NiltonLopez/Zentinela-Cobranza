namespace Zentinela.Application.DTOs.Admin;

public class SupervisorDto
{
    public int    Id              { get; set; }
    public string NombreCompleto  { get; set; } = string.Empty;
    public string Cargo           { get; set; } = string.Empty;
    public long   NumeroDocumento { get; set; }
}

namespace Zentinela.Application.DTOs.Admin;

/// <summary>
/// Fila del listado de Gestión de Agentes.
/// MotivoNoAsignable es null cuando el agente tiene supervisor que es líder activo
/// (es decir, EsLiderSupervisor = true y LiderID != null).
/// </summary>
public class AgenteAdminDto
{
    public long    AgenteID               { get; set; }
    public string  NombreCompleto         { get; set; } = string.Empty;
    public long    NumeroDocumento        { get; set; }
    public string? SupervisorNombre       { get; set; }
    public bool    EsLiderSupervisor      { get; set; }
    public int?    LiderID                { get; set; }
    public string? MotivoNoAsignable      { get; set; }
    public int     TotalCarterasAsignadas { get; set; }
}

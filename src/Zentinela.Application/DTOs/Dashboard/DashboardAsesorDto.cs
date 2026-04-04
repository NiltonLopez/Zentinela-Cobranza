namespace Zentinela.Application.DTOs.Dashboard;

public class DashboardAsesorDto
{
    /// <summary>Nombre completo del supervisor directo (IDSupervisor en RRHH). Null si no tiene asignado.</summary>
    public string? NombreSupervisor { get; set; }

    public IEnumerable<AcuerdoVigenteDto> AcuerdosVigentes { get; set; } = [];

    public RendimientoAsesorDto Rendimiento { get; set; } = new();
}

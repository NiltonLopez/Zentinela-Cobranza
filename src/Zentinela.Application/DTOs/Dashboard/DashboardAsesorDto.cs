namespace Zentinela.Application.DTOs.Dashboard;

public class DashboardAsesorDto
{
    /// <summary>Nombre completo del supervisor directo (IDSupervisor en RRHH). Null si no tiene asignado.</summary>
    public string? NombreSupervisor { get; set; }

    public IEnumerable<AcuerdoVigenteDto> AcuerdosVigentes { get; set; } = [];

    public RendimientoAsesorDto   Rendimiento      { get; set; } = new();
    public PagosConfirmadosDto    PagosConfirmados { get; set; } = new();

    /// <summary>
    /// Categoría de ranking del asesor ("Diamante"/"Oro"/"Plata"/"Bronce"), calculada en vivo
    /// sin persistencia — ver DashboardAsesorService.ObtenerCategoriaRankingAsync.
    /// Null cuando el asesor no tiene honorarios registrados en el año en curso.
    /// </summary>
    public string? CategoriaRanking { get; set; }
}

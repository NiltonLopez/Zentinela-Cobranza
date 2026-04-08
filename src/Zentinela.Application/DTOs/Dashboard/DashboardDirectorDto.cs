namespace Zentinela.Application.DTOs.Dashboard;

public class MetricasDirectorDto
{
    public DateTime? FechaActualizacion  { get; set; }
    public int       TotalGestiones      { get; set; }
    public decimal   TasaContactabilidad { get; set; }
    public decimal   TasaEfectividad     { get; set; }
}

public class FilaLiderDto
{
    public int     LiderID             { get; set; }
    public string  NombreLider         { get; set; } = string.Empty;
    public int     TotalGestiones      { get; set; }
    public decimal TasaContactabilidad { get; set; }
    public decimal TasaEfectividad     { get; set; }
}

public class FilaAsesorDto
{
    public int     AgenteID            { get; set; }
    public string  NombreAsesor        { get; set; } = string.Empty;
    public int     TotalGestiones      { get; set; }
    public decimal TasaContactabilidad { get; set; }
    public decimal TasaEfectividad     { get; set; }
}

public class LiderDropdownDto
{
    public int    ID     { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class CarteraDropdownDto
{
    public int    ID             { get; set; }
    public string NombreCartera  { get; set; } = string.Empty;
}

public class DashboardDirectorDto
{
    public MetricasDirectorDto Metricas    { get; set; } = new();
    /// <summary>Poblado cuando no hay filtro de líder (vista global).</summary>
    public List<FilaLiderDto>  FilasLider  { get; set; } = [];
    /// <summary>Poblado cuando se filtra por un líder específico.</summary>
    public List<FilaAsesorDto> FilasAsesor { get; set; } = [];
}

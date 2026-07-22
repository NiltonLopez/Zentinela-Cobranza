namespace Zentinela.Application.DTOs.Dashboard;

public class MetricasDirectorDto
{
    public DateTime? FechaActualizacion  { get; set; }
    public DateOnly  FechaDesde          { get; set; }
    public DateOnly  FechaHasta          { get; set; }
    public int       TotalGestiones      { get; set; }
    /// <summary>Total de titulares asignados en Titular_Cartera según el filtro activo.</summary>
    public int       TotalAsignados      { get; set; }
    public decimal   TasaContactabilidad { get; set; }
    public decimal   TasaEfectividad     { get; set; }
    /// <summary>Suma de ValorPromesa donde UltimoPerfil es acuerdo o abono.</summary>
    public decimal   ProyeccionPromesas  { get; set; }
    /// <summary>Cantidad de titulares con UltimoPerfil = "Incumplido" en el período.</summary>
    public int       TotalIncumplidos    { get; set; }
    /// <summary>Gestiones con ValorPromesa por encima del techo (ver ADR-026) — no se sumaron a ProyeccionPromesas.</summary>
    public int          GestionesExcluidasPorTecho { get; set; }
    /// <summary>Suma de los ValorPromesa excluidos por superar el techo.</summary>
    public decimal      ValorExcluidoPorTecho      { get; set; }
    public List<long>   TitularesExcluidos         { get; set; } = [];
}

public class FilaLiderDto
{
    public int     LiderID             { get; set; }
    public string  NombreLider         { get; set; } = string.Empty;
    public int     TotalGestiones      { get; set; }
    public decimal TasaContactabilidad { get; set; }
    public decimal TasaEfectividad     { get; set; }
    /// <summary>Suma de ValorPromesa donde UltimoPerfil es acuerdo o abono.</summary>
    public decimal ProyeccionPromesas  { get; set; }
}

public class FilaAsesorDto
{
    public int     AgenteID            { get; set; }
    public string  NombreAsesor        { get; set; } = string.Empty;
    public int     TotalGestiones      { get; set; }
    public decimal TasaContactabilidad { get; set; }
    public decimal TasaEfectividad     { get; set; }
    /// <summary>Suma de ValorPromesa donde UltimoPerfil es acuerdo o abono.</summary>
    public decimal ProyeccionPromesas  { get; set; }
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

/// <summary>
/// Vínculo interno líder↔cartera (Lider_Cartera con EsExterna = 0). Permite que el
/// frontend filtre los selectores de líder y cartera de forma dependiente entre sí.
/// </summary>
public class LiderCarteraVinculoDto
{
    public int LiderID   { get; set; }
    public int CarteraID { get; set; }
}

public class DashboardDirectorDto
{
    public MetricasDirectorDto Metricas    { get; set; } = new();
    /// <summary>Poblado cuando no hay filtro de líder (vista global).</summary>
    public List<FilaLiderDto>  FilasLider  { get; set; } = [];
    /// <summary>Poblado cuando se filtra por un líder específico.</summary>
    public List<FilaAsesorDto> FilasAsesor { get; set; } = [];
}

namespace Zentinela.Application.DTOs.Virtualidad.IVR;

public class InformeIVRDto
{
    public int                       TotalIVR        { get; set; }
    public List<IVRPorLiderCartera>  PorLiderCartera { get; set; } = [];
    public List<AlertaIVRDto>        Alertas         { get; set; } = [];
}

public class IVRPorLiderCartera
{
    public int    LiderID       { get; set; }
    public string NombreLider   { get; set; } = string.Empty;
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = string.Empty;
    public int    Total         { get; set; }
}

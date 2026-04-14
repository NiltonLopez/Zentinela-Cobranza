namespace Zentinela.Application.DTOs.Carteras;

public class RelacionLiderCarteraDto
{
    public int    LiderID           { get; set; }
    public int    CarteraID         { get; set; }
    public string NombreLider       { get; set; } = string.Empty;
    public string NombreCartera     { get; set; } = string.Empty;
    public int    TotalAsignaciones { get; set; }
}

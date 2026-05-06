namespace Zentinela.Application.DTOs.Admin;

/// <summary>
/// Carteras disponibles para asignar al agente, calculadas desde el líder
/// resuelto a partir del supervisor del agente. Las carteras vienen de
/// Lider_Cartera del LiderID; cada item indica si ya está marcada en
/// Cartera_Agente para esa misma combinación (AgenteID, LiderID, CarteraID).
/// </summary>
public class CarterasDelAgenteDto
{
    public int?                      LiderID            { get; set; }
    public string?                   LiderNombre        { get; set; }
    public string?                   MotivoNoAsignable  { get; set; }
    public List<CarteraAgenteItemDto> Carteras           { get; set; } = [];
}

public class CarteraAgenteItemDto
{
    public int    CarteraID { get; set; }
    public string Nombre    { get; set; } = string.Empty;
    public bool   Asignada  { get; set; }
}

namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>Filtro de la auditoría: carteras a auditar + rango de FechaUltimaGestion.</summary>
public class FiltroAuditoria
{
    public List<CarteraSeleccion> Carteras    { get; set; } = [];
    public DateOnly               FechaInicio { get; set; }
    public DateOnly               FechaFin    { get; set; }
}

public class CarteraSeleccion
{
    public int  CarteraID { get; set; }
    public int? LiderID   { get; set; }   // null = cartera completa (todos sus líderes)
}

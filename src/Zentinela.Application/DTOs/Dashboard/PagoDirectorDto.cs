namespace Zentinela.Application.DTOs.Dashboard;

public class PagoDirectorDto
{
    public int      PagoID        { get; set; }
    public long     CedulaTitular { get; set; }
    public string   NombreCliente { get; set; } = string.Empty;
    public decimal  ValorPagado   { get; set; }
    public DateOnly FechaPago     { get; set; }
    public decimal  Honorarios    { get; set; }
    public string   NombreCartera { get; set; } = string.Empty;
    public string   NombreLider   { get; set; } = string.Empty;
    public string   NombreAsesor  { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
}

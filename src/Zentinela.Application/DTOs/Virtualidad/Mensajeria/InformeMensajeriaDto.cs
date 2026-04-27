namespace Zentinela.Application.DTOs.Virtualidad.Mensajeria;

public class InformeMensajeriaDto
{
    public int                              TotalMensajes  { get; set; }
    public List<MensajesPorLiderCartera>    PorLiderCartera { get; set; } = [];
    public List<AlertaMensajeriaDto>        Alertas         { get; set; } = [];
}

public class MensajesPorLiderCartera
{
    public int    LiderID       { get; set; }
    public string NombreLider   { get; set; } = string.Empty;
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = string.Empty;
    public int    Total         { get; set; }
}

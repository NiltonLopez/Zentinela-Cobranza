namespace Zentinela.Application.DTOs.Virtualidad.Mensajeria;

public class InformeMensajeriaDto
{
    public int                        TotalMensajes   { get; set; }
    public List<MensajesPorCartera>   PorCartera      { get; set; } = [];
    public List<AlertaMensajeriaDto>  Alertas         { get; set; } = [];
}

public class MensajesPorCartera
{
    public string NombreCartera { get; set; } = string.Empty;
    public int    Total         { get; set; }
}

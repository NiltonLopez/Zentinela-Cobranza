namespace Zentinela.Application.DTOs.Virtualidad.LlamadasPerdidas;

public class InformeLlamadasDto
{
    public int                          TotalRegistros { get; set; }
    public List<LlamadasPorCartera>     PorCartera     { get; set; } = [];
    public List<AlertaLlamadaDto>       Alertas        { get; set; } = [];
    public List<LlamadaVsGestionDto>    VsGestion      { get; set; } = [];
}

public class LlamadasPorCartera
{
    public string NombreCartera { get; set; } = string.Empty;
    public int    Total         { get; set; }
}

public class AlertaLlamadaDto
{
    public string Celular         { get; set; } = string.Empty;
    public int    VecesComunicado { get; set; }
}

public class LlamadaVsGestionDto
{
    public long    TitularID           { get; set; }
    public string  Celular             { get; set; } = string.Empty;
    public string? UltimoPerfil        { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa       { get; set; }
}

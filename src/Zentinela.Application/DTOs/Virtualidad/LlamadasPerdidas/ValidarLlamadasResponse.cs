namespace Zentinela.Application.DTOs.Virtualidad.LlamadasPerdidas;

public class ValidarLlamadasResponse
{
    public int                    TotalFilas    { get; set; }
    public int                    TotalValidas  { get; set; }
    public int                    TotalInvalidas{ get; set; }
    public List<FilaLlamadaDto>   Preview       { get; set; } = [];
    public List<FilaLlamadaDto>   FilasValidas  { get; set; } = [];
}

public class FilaLlamadaDto
{
    public long   TitularID    { get; set; }
    public string Celular      { get; set; } = string.Empty;
    public string NombreCartera{ get; set; } = string.Empty;
    public int    CarteraID    { get; set; }
    public string Estado       { get; set; } = string.Empty;  // "Válida" | "No vigente — se descartará"
}

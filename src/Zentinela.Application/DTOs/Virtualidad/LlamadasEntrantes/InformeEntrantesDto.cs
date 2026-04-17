namespace Zentinela.Application.DTOs.Virtualidad.LlamadasEntrantes;

public class InformeEntrantesDto
{
    public List<EfectividadSemanal> PorSemana { get; set; } = [];
}

public class EfectividadSemanal
{
    public int    Anio           { get; set; }
    public int    Semana         { get; set; }
    public int    TotalCelulares { get; set; }
    public int    Resueltos      { get; set; }
    public int    ConAcuerdo     { get; set; }
    public double Efectividad    { get; set; }
}

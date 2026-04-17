namespace Zentinela.Application.DTOs.Virtualidad.LlamadasEntrantes;

public class ValidarEntrantesResponse
{
    public int                      TotalCelulares    { get; set; }
    public int                      TotalResueltos    { get; set; }
    public int                      TotalConAcuerdo   { get; set; }
    public double                   Efectividad       { get; set; }
    public List<FilaEntranteDto>    Preview           { get; set; } = [];
    public List<FilaEntranteDto>    FilasParaGuardar  { get; set; } = [];
}

public class FilaEntranteDto
{
    public string Celular      { get; set; } = string.Empty;
    public long?  TitularID    { get; set; }
    public int?   CarteraID    { get; set; }
    public bool?  TieneAcuerdo { get; set; }
    public int    Semana       { get; set; }
    public int    Anio         { get; set; }
}

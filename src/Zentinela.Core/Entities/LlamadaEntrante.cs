namespace Zentinela.Core.Entities;

public class LlamadaEntrante
{
    public int      LlamadaEntranteID { get; set; }
    public string   Celular           { get; set; } = string.Empty;
    public long?    TitularID         { get; set; }
    public int?     CarteraID         { get; set; }
    public bool?    TieneAcuerdo      { get; set; }
    public int      Semana            { get; set; }
    public int      Anio              { get; set; }
    public DateTime FechaCargue       { get; set; }
}

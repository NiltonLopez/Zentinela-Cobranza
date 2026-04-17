namespace Zentinela.Core.Entities;

public class LlamadaPerdida
{
    public int      LlamadaPerdidaID { get; set; }
    public long     TitularID        { get; set; }
    public string   Celular          { get; set; } = string.Empty;
    public int      CarteraID        { get; set; }
    public DateTime FechaCargue      { get; set; }
}

namespace Zentinela.Core.Entities;

public class IVREnvio
{
    public int      IVRID          { get; set; }
    public long     TitularID      { get; set; }
    public string   Celular        { get; set; } = string.Empty;
    public string?  Campana        { get; set; }
    public int?     CarteraID      { get; set; }
    public DateOnly FechaEnvio     { get; set; }
    public string?  Resultado      { get; set; }
    public DateTime FechaCargue    { get; set; }
    public DateTime? FechaResultado { get; set; }
}

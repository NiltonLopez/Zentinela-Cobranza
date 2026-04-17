namespace Zentinela.Core.Entities;

public class MensajeriaEnviada
{
    public int      MensajeriaID    { get; set; }
    public long     NumeroDocumento { get; set; }
    public string   NumeroTelefono  { get; set; } = string.Empty;
    public string   NombreCartera   { get; set; } = string.Empty;
    public int?     CarteraID       { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    public DateTime FechaCargue     { get; set; }
}

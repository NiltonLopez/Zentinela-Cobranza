namespace Zentinela.Core.Entities;

/// <summary>
/// Registro de mensajes salientes enviados a titulares.
/// Tabla de solo inserción — sin PK surrogate (HasNoKey).
/// Canal permite registrar futuros canales (WhatsApp, Email, etc.).
/// </summary>
public class MensajeriaEnviada
{
    public long     NumeroDocumento { get; set; }
    public string   NumeroTelefono  { get; set; } = string.Empty;
    public int?     CarteraID       { get; set; }
    public string   NombreCartera   { get; set; } = string.Empty;
    public DateOnly FechaEnvio      { get; set; }
    public string   Canal           { get; set; } = "SMS";
    public DateTime FechaCargue     { get; set; }
}

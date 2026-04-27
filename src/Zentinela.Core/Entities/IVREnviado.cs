namespace Zentinela.Core.Entities;

/// <summary>
/// Registro de envíos IVR (voz automatizada) a titulares.
/// Tabla de solo inserción — sin PK surrogate (HasNoKey).
/// Espejo de MensajeriaEnviada con Canal por defecto 'IVR'.
/// </summary>
public class IVREnviado
{
    public long     NumeroDocumento { get; set; }
    public string   NumeroTelefono  { get; set; } = string.Empty;
    public int      LiderID         { get; set; }
    public int      CarteraID       { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    public string   Canal           { get; set; } = "IVR";
    public DateTime FechaCargue     { get; set; }
}

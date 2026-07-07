namespace Zentinela.Core.Entities;

/// <summary>
/// Solicitud de virtualidad (envío IVR / SMS / Correo) a un titular, con su
/// resultado actualizable. Una sola tabla con discriminador <see cref="Canal"/>:
///   - Fase Solicitud: se inserta con <see cref="Estado"/> = null (sin resultado).
///   - Fase Resultado: se actualiza <see cref="Estado"/> + <see cref="FechaResultado"/> por PK.
/// El par (LiderID, CarteraID) se ancla a Lider_Cartera (ver ADR-014).
/// </summary>
public class SolicitudVirtualidad
{
    public long      ID              { get; set; }
    public string    Canal           { get; set; } = string.Empty; // IVR | SMS | Correo
    public long      NumeroDocumento { get; set; }
    public string    Destino         { get; set; } = string.Empty; // teléfono o email
    public int       CarteraID       { get; set; }
    public int       LiderID         { get; set; }
    public DateOnly  FechaEnvio      { get; set; }
    public string?   Estado          { get; set; }                 // null = sin resultado
    public DateTime? FechaResultado  { get; set; }
    public DateTime  FechaCargue     { get; set; }
}

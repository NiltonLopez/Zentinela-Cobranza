namespace Zentinela.Core.Entities;

/// <summary>
/// Pago confirmado cargado por la encargada (Valentina) desde un CSV por cartera.
/// Reside en DBCallCenter para mantener FK nativas con Titular, Lider y Cartera.
/// UsuarioRegistro guarda el UserName (ApplicationUser.UserName de DBZentinela) del
/// usuario que confirmó el cargue — no su Id. Cross-DB, sin FK de BD.
/// </summary>
public class Pago
{
    public int     PagoID           { get; set; }
    public long    CedulaTitular    { get; set; }
    public string  NombreCliente    { get; set; } = string.Empty;
    public decimal ValorPagado      { get; set; }
    public DateOnly FechaPago       { get; set; }
    public decimal Honorarios       { get; set; }
    public int     LiderID          { get; set; }
    public int     CarteraID        { get; set; }
    /// <summary>UserName (login de Identity) del usuario que confirmó el cargue del pago.</summary>
    public string  UsuarioRegistro  { get; set; } = string.Empty;
    /// <summary>
    /// Cédula del agente responsable del titular. FK a Agente.AgenteID.
    /// NULL cuando el pago entró sin mérito de asesor (UsuarioControl = "BASE GENERAL"
    /// en el cargue) y el recaudo se atribuye solamente al líder.
    /// </summary>
    public long?   AgenteControlID  { get; set; }
    public DateTime FechaRegistro   { get; set; }
}

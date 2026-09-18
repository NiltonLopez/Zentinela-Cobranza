namespace Zentinela.Core.Entities;

/// <summary>
/// Número corporativo y link de WhatsApp de un asesor, para incluirlos en los SMS.
/// Existe porque <see cref="Agente"/> no tiene teléfono y el de RRHH es el personal.
/// 1:1 con Agente por AgenteID; el link puede ser el wa.me generado o un acortado
/// pegado a mano.
/// </summary>
public class AgenteContactoVirtualidad
{
    public long     AgenteID             { get; set; }
    public string?  NumeroCorporativo    { get; set; }
    public string?  LinkWhatsapp         { get; set; }
    public DateTime FechaActualizacion   { get; set; }
    public string   UsuarioActualizacion { get; set; } = string.Empty;
}

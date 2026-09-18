namespace Zentinela.Core.Entities;

/// <summary>
/// Representa un agente registrado en DBCallCenter.
/// AgenteID coincide con el NumeroDocumento del empleado en RRHH.
/// </summary>
public class Agente
{
    public long AgenteID { get; set; }
    public string? UsuarioControlNext { get; set; }
    public bool Activo { get; set; }

    // Contacto para los SMS del Creador de Virtualidad (ADR-037). El teléfono de RRHH es
    // el personal; este es el corporativo. El link puede ser el wa.me generado o un
    // acortado pegado a mano. Nullable: se llena a medida que los líderes lo usan.
    public string?   NumeroCorporativo      { get; set; }
    public string?   LinkWhatsapp           { get; set; }
    public DateTime? ContactoActualizadoEn  { get; set; }
    public string?   ContactoActualizadoPor { get; set; }
}

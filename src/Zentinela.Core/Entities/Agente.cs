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
}

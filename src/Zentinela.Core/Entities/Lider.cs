namespace Zentinela.Core.Entities;

public class Lider
{
    public int ID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Activo { get; set; }

    /// <summary>
    /// Cédula del empleado en DBRecursosHumanos.
    /// Vínculo entre el líder de cobranza y su registro de empleado.
    /// </summary>
    public long? NumeroDocumento { get; set; }
}

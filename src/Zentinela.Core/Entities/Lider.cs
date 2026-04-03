namespace Zentinela.Core.Entities;

public class Lider
{
    public int ID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Activo { get; set; }
}

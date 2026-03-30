namespace Zentinela.API.Domain.Entities;

public class Lider
{
    public int ID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    // ApplicationUser asociado (relación inversa)
    public ApplicationUser? Usuario { get; set; }
}

namespace Zentinela.API.Domain.Entities;

public class Cartera
{
    public int ID { get; set; }
    public string NombreCartera { get; set; } = string.Empty;
    public int? LiderID { get; set; }
    public Lider? Lider { get; set; }
}

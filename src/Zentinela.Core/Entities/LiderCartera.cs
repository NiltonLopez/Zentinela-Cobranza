namespace Zentinela.Core.Entities;

public class LiderCartera
{
    public int LiderID { get; set; }
    public Lider Lider { get; set; } = null!;
    public int CarteraID { get; set; }
    public Cartera Cartera { get; set; } = null!;
}

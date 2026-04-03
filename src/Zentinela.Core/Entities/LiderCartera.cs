namespace Zentinela.Core.Entities;

public class LiderCartera
{
    public int LiderID { get; set; }
    public Lider Lider { get; set; } = null!;
    // En BD la columna se llama CartID (no CarteraID)
    public int CartID { get; set; }
    public Cartera Cartera { get; set; } = null!;
}

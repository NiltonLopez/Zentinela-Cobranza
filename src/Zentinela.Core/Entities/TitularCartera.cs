namespace Zentinela.Core.Entities;

// Pivote principal del negocio: asigna titulares a carteras por líder
public class TitularCartera
{
    public long TitularID { get; set; }
    public Titular Titular { get; set; } = null!;
    public int LiderID { get; set; }
    public Lider Lider { get; set; } = null!;
    public int CarteraID { get; set; }
    public Cartera Cartera { get; set; } = null!;
}

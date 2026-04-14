using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Titular_Cartera")]
public class TitularCartera
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [Column("LiderID")]
    public int LiderId { get; set; }

    [Column("CarteraID")]
    public int CarteraId { get; set; }

    public virtual Titular Titular { get; set; } = null!;
    public virtual Lider Lider { get; set; } = null!;
    public virtual Cartera Cartera { get; set; } = null!;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("DatosClaro")]
public class DatosClaro
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(10)]
    public string? NumeroTelefono { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    public DateTime? FechaInsercion { get; set; }
}

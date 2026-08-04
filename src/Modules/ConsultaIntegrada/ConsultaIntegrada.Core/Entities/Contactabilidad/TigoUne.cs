using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("TigoUne")]
public class TigoUne
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? NumeroTelefono { get; set; }

    [StringLength(255)]
    public string? Email { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

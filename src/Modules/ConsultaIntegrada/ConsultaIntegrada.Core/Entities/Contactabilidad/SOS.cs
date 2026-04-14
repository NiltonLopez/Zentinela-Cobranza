using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("SOS")]
public class SOS
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? Telefono { get; set; }

    [StringLength(70)]
    public string? Direccion { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

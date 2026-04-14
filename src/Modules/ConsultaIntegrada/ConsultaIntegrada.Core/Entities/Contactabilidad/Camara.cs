using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("Camara")]
public class Camara
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? NumeroTelefono { get; set; }

    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(70)]
    public string? Municipio { get; set; }

    [StringLength(70)]
    public string? Departamento { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

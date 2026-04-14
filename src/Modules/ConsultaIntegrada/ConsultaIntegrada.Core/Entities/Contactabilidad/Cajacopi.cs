using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("Cajacopi")]
public class Cajacopi
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? NumeroTelefonoCelular { get; set; }

    [StringLength(100)]
    public string? NumeroTelefonoFijo { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(70)]
    public string? Departamento { get; set; }

    [StringLength(70)]
    public string? Municipio { get; set; }

    public DateTime? FechaNacimiento { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

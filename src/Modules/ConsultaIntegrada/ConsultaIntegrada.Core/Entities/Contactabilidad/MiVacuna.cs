using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("MiVacuna")]
public class MiVacuna
{
    [Key]
    public int TitularID { get; set; }

    public DateTime? FechaExpedicion { get; set; }

    [StringLength(10)]
    public string? Celular { get; set; }

    [StringLength(255)]
    public string? Email { get; set; }

    public bool? EstadoEnMiVacuna { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

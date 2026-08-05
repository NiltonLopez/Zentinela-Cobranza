using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("SaviaSalud")]
public class SaviaSalud
{
    [Key]
    public int TitularID { get; set; }

    public DateOnly? FechaDeNacimiento { get; set; }

    [StringLength(50)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    public string? EstadoDeAfiliacion { get; set; }

    [StringLength(70)]
    public string? Regimen { get; set; }

    [StringLength(100)]
    public string? NumeroCelular { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

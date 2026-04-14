using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosPersonales;

[Table("Fosyga")]
public class Fosyga
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? EPS { get; set; }

    [StringLength(50)]
    public string? Regimen { get; set; }

    public DateTime? FechaAfiliacionEfectiva { get; set; }

    [StringLength(50)]
    public string? TipoAfiliado { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

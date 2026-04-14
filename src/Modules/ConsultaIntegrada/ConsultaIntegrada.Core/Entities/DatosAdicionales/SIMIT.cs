using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosAdicionales;

[Table("SIMIT")]
public class SIMIT
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(10)]
    public string? Placa { get; set; }

    public int? EstadoEnSIMIT { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

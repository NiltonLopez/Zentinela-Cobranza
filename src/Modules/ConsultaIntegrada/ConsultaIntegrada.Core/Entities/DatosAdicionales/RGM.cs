using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosAdicionales;

[Table("RGM")]
public class RGM
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(50)]
    public string? Marca { get; set; }

    public int? Modelo { get; set; }

    [StringLength(50)]
    public string? Placa { get; set; }

    public int? EstadoEnRGM { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

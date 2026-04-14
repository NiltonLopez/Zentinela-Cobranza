using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosLaborales;

[Table("RUES")]
public class RUES
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(20)]
    public string? NumeroMatricula { get; set; }

    [StringLength(20)]
    public string? EstadoEmpresa { get; set; }

    [StringLength(70)]
    public string? ActividadEconomica { get; set; }

    [StringLength(100)]
    public string? NombreEmpresa { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

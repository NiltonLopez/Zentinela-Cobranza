using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosLaborales;

[Table("PositivaEmpresas")]
public class PositivaEmpresas
{
    [Key]
    public int TitularID { get; set; }

    public long? NIT { get; set; }

    [StringLength(230)]
    public string? Empresa { get; set; }

    [StringLength(230)]
    public string? EstadoAfiliacion { get; set; }

    [StringLength(100)]
    public string? EstadoEnPositivaEmpresa { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

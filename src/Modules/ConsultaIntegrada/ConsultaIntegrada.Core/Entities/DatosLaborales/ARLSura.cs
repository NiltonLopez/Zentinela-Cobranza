using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.DatosLaborales;

[Table("ARLSura")]
public class ARLSura
{
    [Key]
    public int TitularID { get; set; }

    public long? NIT { get; set; }

    [StringLength(100)]
    public string? Empresa { get; set; }

    [StringLength(100)]
    public string? EstadoEnSura { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

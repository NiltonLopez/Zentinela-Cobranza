using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("MutualSer")]
public class MutualSer
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(10)]
    public string? NumeroTelefonoCelular { get; set; }

    [StringLength(10)]
    public string? NumeroTelefonoCelular2 { get; set; }

    [StringLength(10)]
    public string? NumeroTelefonoFijo { get; set; }

    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Regimen { get; set; }

    [StringLength(50)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    public string? Departamento { get; set; }

    [StringLength(50)]
    public string? Municipio { get; set; }

    public bool EstadoEnMutualSer { get; set; }

    public DateTime FechaConsulta { get; set; }
}

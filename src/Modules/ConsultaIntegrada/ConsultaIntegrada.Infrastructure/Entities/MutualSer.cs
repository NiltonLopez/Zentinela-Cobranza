using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("MutualSer")]
public partial class MutualSer
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefonoCelular { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefonoCelular2 { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefonoFijo { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Email { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? Regimen { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Departamento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipio { get; set; }

    public bool EstadoEnMutualSer { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("RUES")]
public partial class Rue
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    public long? NumeroMatricula { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? EstadoEmpresa { get; set; }

    [StringLength(250)]
    [Unicode(false)]
    public string? ActividadEconomica { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string? NombreEmpresa { get; set; }

    [Column("EstadoEnRUES")]
    public bool EstadoEnRues { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaConsulta { get; set; }
}

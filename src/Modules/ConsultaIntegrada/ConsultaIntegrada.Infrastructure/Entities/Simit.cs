using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("SIMIT")]
public partial class Simit
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? Placa { get; set; }

    [Column("EstadoEnSIMIT")]
    public bool EstadoEnSimit { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

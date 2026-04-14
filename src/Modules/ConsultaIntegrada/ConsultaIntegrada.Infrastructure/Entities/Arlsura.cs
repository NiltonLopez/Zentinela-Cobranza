using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("ARLSura")]
public partial class Arlsura
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [Column("NIT")]
    public long? Nit { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Empresa { get; set; }

    public bool EstadoEnSura { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

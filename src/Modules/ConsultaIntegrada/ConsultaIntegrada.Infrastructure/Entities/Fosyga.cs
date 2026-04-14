using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Fosyga")]
public partial class Fosyga
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? Estado { get; set; }

    [Column("EPS")]
    [StringLength(120)]
    [Unicode(false)]
    public string? Eps { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? Regimen { get; set; }

    public DateOnly? FechaAfiliacionEfectiva { get; set; }

    public DateOnly? FechaFinalizacionAfiliacion { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string? Departamento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipio { get; set; }

    public bool EstadoEnFosyga { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

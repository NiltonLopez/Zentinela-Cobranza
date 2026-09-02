using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

/// <summary>
/// Contactabilidad + afiliación Sura (1:1 por titular). Distinta de
/// <see cref="Arlsura"/>, que es el producto de riesgos laborales: esta trae
/// celular, email y datos de afiliación. Único teléfono: NumeroCelular.
/// </summary>
[Table("Sura")]
public partial class Sura
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroCelular { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Email { get; set; }

    public long? NIT { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Empresa { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    public DateOnly? UltimaFechaConsulta { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EstadoAfiliacion { get; set; }

    public bool EstadoEnSura { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

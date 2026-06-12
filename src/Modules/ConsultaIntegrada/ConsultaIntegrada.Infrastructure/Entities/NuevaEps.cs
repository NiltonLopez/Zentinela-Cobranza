using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

/// <summary>
/// Contactabilidad + afiliación NuevaEps (1:1 por titular). A diferencia de las demás
/// fuentes, TitularID es varchar(30) (no bigint) — se compara como string.
/// </summary>
[Table("NuevaEps")]
public partial class NuevaEps
{
    [Column("TitularID")]
    [StringLength(30)]
    [Unicode(false)]
    public string? TitularID { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefono { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? EstadoAfiliacion { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Departamento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipio { get; set; }

    public bool EstadoEnNuevaEps { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

/// <summary>
/// Contactabilidad + afiliación Savia Salud (1:1 por titular). TitularID es bigint
/// (nullable en origen). Celular en NumeroCelular; NumeroTelefono está vacío y se
/// omite. Sin email.
/// </summary>
[Table("SaviaSalud")]
public partial class SaviaSalud
{
    [Column("TitularID")]
    public long? TitularId { get; set; }

    public DateOnly? FechaDeNacimiento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EstadoDeAfiliacion { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string? Regimen { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? NumeroCelular { get; set; }

    public bool? Estado { get; set; }

    public DateOnly? FechaConsulta { get; set; }
}

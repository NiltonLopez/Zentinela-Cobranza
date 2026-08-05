using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

/// <summary>
/// Contactabilidad + afiliación Coosalud (1:1 por titular). TitularID es bigint
/// (como Tigo/TigoUne), a diferencia de NuevaEps que es varchar.
/// </summary>
[Table("Coosalud")]
public partial class Coosalud
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefono { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Email { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? MunicipioOrigen { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? DepartamentoOrigen { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Regimen { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? EstadoAfiliacion { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? NivelSisben { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    public bool EstadoEnCoosalud { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

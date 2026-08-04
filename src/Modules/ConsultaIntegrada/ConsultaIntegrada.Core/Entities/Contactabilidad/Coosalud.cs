using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConsultaIntegrada.Core.Entities.Contactabilidad;

[Table("Coosalud")]
public class Coosalud
{
    [Key]
    public int TitularID { get; set; }

    [StringLength(100)]
    public string? NumeroTelefono { get; set; }

    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? MunicipioOrigen { get; set; }

    [StringLength(50)]
    public string? DepartamentoOrigen { get; set; }

    [StringLength(50)]
    public string? Regimen { get; set; }

    [StringLength(50)]
    public string? TipoAfiliado { get; set; }

    [StringLength(50)]
    public string? EstadoAfiliacion { get; set; }

    [StringLength(50)]
    public string? NivelSisben { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    public DateTime? FechaConsulta { get; set; }
}

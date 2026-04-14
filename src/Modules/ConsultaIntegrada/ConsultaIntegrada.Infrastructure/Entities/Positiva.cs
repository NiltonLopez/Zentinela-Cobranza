using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Positiva")]
public partial class Positiva
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefonoCelular { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? NumeroTelefonoFijo { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Email { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Departamento { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipio { get; set; }

    public bool EstadoEnPositiva { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

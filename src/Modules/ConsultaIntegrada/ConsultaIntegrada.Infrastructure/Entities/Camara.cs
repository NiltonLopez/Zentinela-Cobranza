using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Camara")]
public partial class Camara
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
    public string? Municipio { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string? Departamento { get; set; }

    public bool EstadoEnCamara { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

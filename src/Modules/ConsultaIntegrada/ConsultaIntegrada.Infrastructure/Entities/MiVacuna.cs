using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("MiVacuna")]
public partial class MiVacuna
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    public DateOnly? FechaExpedicion { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? Celular { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Email { get; set; }

    public bool? EstadoEnMiVacuna { get; set; }

    public DateOnly? FechaConsulta { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Lider")]
public partial class Lider
{
    [Key]
    [Column("ID")]
    public int Id { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string Nombre { get; set; } = null!;

    [StringLength(150)]
    [Unicode(false)]
    public string? Email { get; set; }

    public bool Activo { get; set; }

    public long? NumeroDocumento { get; set; }
}

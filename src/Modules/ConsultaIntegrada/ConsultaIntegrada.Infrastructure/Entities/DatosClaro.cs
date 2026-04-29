using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("DatosClaro")]
public partial class DatosClaro
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefono { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Email { get; set; }

    public DateOnly? FechaInsercion { get; set; }
}

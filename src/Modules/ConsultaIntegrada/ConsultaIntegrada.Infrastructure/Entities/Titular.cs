using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("Titular")]
[Index("NumeroDocumento", Name = "Titular_Unique", IsUnique = true)]
public partial class Titular
{
    [Key]
    [Column("NumeroDocumento")]
    public long NumeroDocumento { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string Nombre { get; set; } = null!;

    public long? NitEmpresa { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    public DateOnly? FechaExpedicionDocumento { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("SaludTotal")]
public partial class SaludTotal
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? NumeroTelefono { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Email { get; set; }

    [Column("NIT")]
    public long? Nit { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? Empresa { get; set; }

    [StringLength(60)]
    [Unicode(false)]
    public string? Cargo { get; set; }

    public DateOnly? UltimaFechaPago { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? IngresoBase { get; set; }

    public bool EstadoEnSaludTotal { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

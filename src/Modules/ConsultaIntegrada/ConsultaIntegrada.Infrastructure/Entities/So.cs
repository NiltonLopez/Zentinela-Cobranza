using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("SOS")]
public partial class So
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    public long? NitEmpresa { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? RazonSocial { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string? Telefono { get; set; }

    [StringLength(70)]
    [Unicode(false)]
    public string? Direccion { get; set; }

    [Column("EstadoEnSOS")]
    public bool EstadoEnSos { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

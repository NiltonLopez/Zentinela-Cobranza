using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

[Table("RGM")]
public partial class Rgm
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Marca { get; set; }

    public int? Modelo { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Placa { get; set; }

    [Column("EstadoEnRGM")]
    public bool? EstadoEnRgm { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

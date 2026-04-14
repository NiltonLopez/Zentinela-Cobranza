using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

public partial class PositivaEmpresa
{
    [Column("TitularID")]
    public long TitularId { get; set; }

    [Column("NIT")]
    public long? Nit { get; set; }

    [StringLength(230)]
    [Unicode(false)]
    public string? Empresa { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string? EstadoAfiliacion { get; set; }

    public bool EstadoEnPositivaEmpresa { get; set; }

    public DateOnly FechaConsulta { get; set; }
}

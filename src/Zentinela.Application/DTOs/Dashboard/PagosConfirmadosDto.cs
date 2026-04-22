namespace Zentinela.Application.DTOs.Dashboard;

public class PagosConfirmadosDto
{
    /// <summary>
    /// Fecha del pago más antiguo entre los últimos pagos registrados por cartera.
    /// Si el asesor tiene 2 carteras, muestra la que quedó más tiempo sin actualizar.
    /// </summary>
    public DateTime? FechaActualizacion { get; set; }

    public decimal TotalRecaudo       { get; set; }
    public decimal TotalHonorarios    { get; set; }
    public decimal MetaHonorarios     { get; set; }

    /// <summary>Acuerdos/abonos sobre total de gestiones (0-1).</summary>
    public decimal PorcentajeEfectividad { get; set; }
}

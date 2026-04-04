namespace Zentinela.Application.DTOs.Dashboard;

public class RendimientoAsesorDto
{
    /// <summary>
    /// Fecha y hora del último ciclo de procesamiento de datos.
    /// Null si no hay datos para el asesor.
    /// </summary>
    public DateTime? FechaActualizacion { get; set; }

    /// <summary>Total de gestiones en el día del reporte.</summary>
    public int TotalGestiones { get; set; }

    /// <summary>Contactos positivos / total gestiones.</summary>
    public decimal TasaContactabilidad { get; set; }

    /// <summary>Acuerdos+abonos / contactos positivos.</summary>
    public decimal TasaEfectividad { get; set; }
}

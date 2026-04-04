namespace Zentinela.Application.DTOs.Dashboard;

public class AcuerdoVigenteDto
{
    public long TitularID { get; set; }
    public string NombreTitular { get; set; } = string.Empty;
    public string UltimoPerfil { get; set; } = string.Empty;
    public DateOnly FechaPromesa { get; set; }
    public decimal? ValorPromesa { get; set; }
    public decimal? SaldoTotal { get; set; }

    /// <summary>
    /// "rojo" = vencido (FechaPromesa &lt;= hoy),
    /// "amarillo" = próximo (hasta 3 días),
    /// "verde" = a tiempo (&gt; 3 días).
    /// </summary>
    public string Semaforo { get; set; } = string.Empty;
}

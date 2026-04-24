namespace Zentinela.Core.Entities;

/// <summary>
/// Estado histórico por titular, cartera y agente.
/// Fuente para las métricas de rendimiento del asesor.
/// </summary>
public class HistoricoEstadoTitular
{
    public long TitularID { get; set; }
    public int CarteraID { get; set; }
    public int AgenteID { get; set; }
    public string? UltimoPerfil { get; set; }
    public string? MejorPerfil { get; set; }

    public decimal? SaldoTotal { get; set; }
    public string? NumeroMarcado { get; set; }
    public int? Rotacion { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa { get; set; }
    public decimal? ValorPromesa { get; set; }
    public string?   MotivoNoPago       { get; set; }
    public DateTime  FechaActualizacion { get; set; }
}

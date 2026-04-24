namespace Zentinela.Core.Entities;

/// <summary>
/// Estado actual (snapshot) de cada titular por cartera y agente.
/// FechaActualizacion indica cuándo el sistema procesó el último ciclo de datos.
/// </summary>
public class UltimoEstadoTitular
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
    public string? MotivoNoPago { get; set; }
    public DateTime FechaActualizacion { get; set; }

    public Titular Titular { get; set; } = null!;
}

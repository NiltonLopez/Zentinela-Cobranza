namespace Zentinela.Application.DTOs.Virtualidad.IVR;

/// <summary>
/// Una fila del Excel del informe IVR: un cliente marcado en el período, por líder y cartera
/// (la misma unidad que cuenta el análisis, ADR-031). Es lo que el líder usa para armar
/// estrategias: a quién se marcó, qué pasó, cómo está su gestión y si pagó después.
/// </summary>
public class DetalleClienteIvrDto
{
    public long    Cedula   { get; set; }
    public string? Nombre   { get; set; }
    public string  Lider    { get; set; } = string.Empty;
    public string  Cartera  { get; set; } = string.Empty;
    /// <summary>Teléfonos marcados en el período, distintos, separados por coma.</summary>
    public string? Telefonos { get; set; }

    public int      Marcaciones      { get; set; }
    public DateOnly PrimeraMarcacion { get; set; }
    public DateOnly UltimaMarcacion  { get; set; }
    /// <summary>Rango del mejor desenlace (1 = marcó opción 1 … 5 = colgó; 98 sin resultado; 99 sin contacto).</summary>
    public int      RangoDesenlace   { get; set; }
    public string   MejorDesenlace   { get; set; } = string.Empty;
    /// <summary>Estado de la marcación más reciente, tal como lo reportó el IVR.</summary>
    public string?  UltimoResultado  { get; set; }

    // ── Estado de gestión actual (UltimoEstadoTitular) ──────────────────────
    public string?   UltimoPerfil       { get; set; }
    public string?   MejorPerfil        { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa       { get; set; }
    public decimal?  ValorPromesa       { get; set; }
    public decimal?  SaldoTotal         { get; set; }

    // ── Pagos a esta cartera desde la primera marcación del período ─────────
    public int       PagosPosteriores  { get; set; }
    public decimal   ValorPagado       { get; set; }
    public DateOnly? FechaUltimoPago   { get; set; }
}

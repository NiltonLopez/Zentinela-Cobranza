namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>
/// Estimación del costo (en pesos colombianos) de evaluar con IA las gestiones
/// del filtro seleccionado. Los tokens de entrada se cuentan con :countTokens
/// (gratis); los de salida se estiman por registro.
/// </summary>
public class EstimacionCostoResponse
{
    public int     RegistrosIA   { get; set; }   // gestiones con texto que irían a la IA
    public int     TokensEntrada { get; set; }
    public int     TokensSalida  { get; set; }   // estimado
    public decimal CostoEntrada  { get; set; }   // COP
    public decimal CostoSalida   { get; set; }   // COP
    public decimal CostoTotal    { get; set; }   // COP
}

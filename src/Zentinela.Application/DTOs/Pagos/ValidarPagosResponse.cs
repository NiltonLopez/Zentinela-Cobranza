namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Resultado de la validación del CSV de pagos.
/// Preview contiene las primeras 10 filas (válidas e inválidas) para mostrar en la UI.
/// TodasLasValidas contiene todas las filas válidas para enviar al endpoint de confirmación.
/// </summary>
public class ValidarPagosResponse
{
    public int                TotalFilas       { get; set; }
    public int                CantidadValidas  { get; set; }
    public int                CantidadErrores  { get; set; }
    public List<PagoFilaDto>  Preview          { get; set; } = [];
    public List<PagoFilaDto>  TodasLasValidas  { get; set; } = [];
}

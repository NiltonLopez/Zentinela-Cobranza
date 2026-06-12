namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Resultado de la validación del CSV de pagos.
/// Preview contiene las primeras 10 filas válidas (no duplicadas) para la vista previa.
/// Errores contiene TODAS las filas con error para que el usuario pueda inspeccionarlas.
/// Duplicados contiene TODAS las filas que ya estaban cargadas (misma cédula, fecha y valor
/// en la cartera) o repetidas dentro del archivo — son válidas pero NO se insertan.
/// TodasLasValidas contiene las filas válidas NO duplicadas para enviar al endpoint de confirmación.
/// </summary>
public class ValidarPagosResponse
{
    public int                TotalFilas             { get; set; }
    public int                CantidadValidas        { get; set; }
    public int                CantidadDuplicados     { get; set; }
    public int                CantidadErrores        { get; set; }
    /// <summary>Cuántas de las válidas crearán un titular nuevo (subconjunto de las válidas).</summary>
    public int                CantidadTitularesNuevos { get; set; }
    public List<PagoFilaDto>  Preview            { get; set; } = [];
    public List<PagoFilaDto>  Duplicados         { get; set; } = [];
    public List<PagoFilaDto>  Errores            { get; set; } = [];
    /// <summary>Filas válidas cuyo titular no existe aún (se creará al confirmar). Subconjunto de TodasLasValidas.</summary>
    public List<PagoFilaDto>  TitularesNuevos    { get; set; } = [];
    public List<PagoFilaDto>  TodasLasValidas    { get; set; } = [];
}

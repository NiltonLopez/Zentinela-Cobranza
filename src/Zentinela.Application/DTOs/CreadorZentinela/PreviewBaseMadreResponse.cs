namespace Zentinela.Application.DTOs.CreadorZentinela;

public class PreviewBaseMadreResponse
{
    /// <summary>Total de registros que cumplen los filtros (sin límite de paginación).</summary>
    public int Total { get; set; }

    /// <summary>
    /// Primeras N filas como diccionarios { nombreColumna → valor }.
    /// El frontend renderiza dinámicamente según las columnas seleccionadas.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> Filas { get; set; } = [];
}

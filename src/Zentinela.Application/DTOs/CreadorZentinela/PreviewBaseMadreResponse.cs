namespace Zentinela.Application.DTOs.CreadorZentinela;

public class PreviewBaseMadreResponse
{
    /// <summary>
    /// Total de registros que cumplen los filtros. Cuando <see cref="TotalExacto"/> es false,
    /// es una cota inferior (el frontend muestra "más de N").
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// True = <see cref="Total"/> es el conteo exacto. False = es cota inferior ("más de N"):
    /// ocurre con filtros aplicados cuando el resultado supera el límite de la preview, para
    /// evitar el COUNT exacto sobre la TVF (que tarda minutos en carteras grandes).
    /// </summary>
    public bool TotalExacto { get; set; } = true;

    /// <summary>
    /// Primeras N filas como diccionarios { nombreColumna → valor }.
    /// El frontend renderiza dinámicamente según las columnas seleccionadas.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> Filas { get; set; } = [];
}

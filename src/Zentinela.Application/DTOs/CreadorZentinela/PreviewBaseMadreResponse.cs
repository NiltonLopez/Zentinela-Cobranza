using Zentinela.Core.Interfaces;

namespace Zentinela.Application.DTOs.CreadorZentinela;

public class PreviewBaseMadreResponse
{
    /// <summary>Total de registros que cumplen los filtros. Su precisión la indica <see cref="TotalTipo"/>.</summary>
    public int Total { get; set; }

    /// <summary>
    /// Precisión del <see cref="Total"/>: Exacto, Aproximado (sin filtros, muchas filas → conteo
    /// por Titular_Cartera) o Minimo (con filtros, muchas filas → "más de N"). Evita el COUNT
    /// exacto sobre la TVF, que tarda minutos en carteras grandes.
    /// </summary>
    public TipoTotalPreview TotalTipo { get; set; } = TipoTotalPreview.Exacto;

    /// <summary>
    /// Primeras N filas como diccionarios { nombreColumna → valor }.
    /// El frontend renderiza dinámicamente según las columnas seleccionadas.
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> Filas { get; set; } = [];
}

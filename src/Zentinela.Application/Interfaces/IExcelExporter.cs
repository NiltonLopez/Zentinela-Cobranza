using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;

namespace Zentinela.Application.Interfaces;

public interface IExcelExporter
{
    byte[] GenerarBaseMadre(
        IEnumerable<Dictionary<string, object?>> filas,
        IEnumerable<ColumnaBaseMadre> columnas,
        string nombreCartera);

    byte[] GenerarPlantillaAsignaciones();

    /// <summary>
    /// Parsea el Excel de asignaciones y devuelve las filas como pares (NumeroDocumento, Nombre).
    /// La columna A es NumeroDocumento (obligatoria); la columna B es Nombre (opcional).
    /// </summary>
    List<(long NumeroDocumento, string Nombre)> ParsearFilasAsignacion(Stream stream);

    byte[] GenerarPlantillaReporte();

    List<Application.DTOs.Reportes.FilaReporteRaw> ParsearFilasReporte(Stream stream, string extension);

    /// <summary>
    /// Genera el Excel de descarga del Comparador de Carteras con todos los titulares coincidentes.
    /// </summary>
    byte[] GenerarComparativoCartera(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada,
        string nombreOtraCartera);

    /// <summary>
    /// Genera el Excel completo del Comparador de Carteras con todas las carteras combinadas.
    /// Incluye una columna "Cartera de referencia" que identifica la cartera de cada fila.
    /// </summary>
    byte[] GenerarComparativoCompleto(
        IEnumerable<TitularComparativoDto> titulares,
        string nombreCarteraSeleccionada);
}

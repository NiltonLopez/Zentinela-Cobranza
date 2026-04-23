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
}

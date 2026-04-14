using Zentinela.Application.DTOs.CreadorZentinela;

namespace Zentinela.Application.Interfaces;

public interface IExcelExporter
{
    byte[] GenerarBaseMadre(
        IEnumerable<Dictionary<string, object?>> filas,
        IEnumerable<ColumnaBaseMadre> columnas,
        string nombreCartera);

    /// <summary>
    /// Genera la plantilla de asignaciones masivas: dos columnas,
    /// NumeroDocumento (obligatorio) y Nombre (referencia).
    /// </summary>
    byte[] GenerarPlantillaAsignaciones();

    /// <summary>
    /// Parsea el Excel de asignaciones y devuelve los valores de la columna A
    /// (fila 2 en adelante) como lista de long (NumeroDocumento).
    /// Ignora celdas vacías o no numéricas.
    /// </summary>
    List<long> ParsearNumerosDocumento(Stream stream);

    /// <summary>
    /// Genera la plantilla Excel para cargue de reportes (14 columnas).
    /// </summary>
    byte[] GenerarPlantillaReporte();

    /// <summary>
    /// Parsea el Excel de reportes y devuelve una fila raw por cada registro
    /// (fila 2 en adelante). Ignora filas completamente vacías.
    /// </summary>
    List<Application.DTOs.Reportes.FilaReporteRaw> ParsearFilasReporte(Stream stream);
}

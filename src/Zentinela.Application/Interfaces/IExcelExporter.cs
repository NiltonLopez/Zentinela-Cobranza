using Zentinela.Application.DTOs.CreadorZentinela;

namespace Zentinela.Application.Interfaces;

public interface IExcelExporter
{
    byte[] GenerarBaseMadre(
        IEnumerable<Dictionary<string, object?>> filas,
        IEnumerable<ColumnaBaseMadre> columnas,
        string nombreCartera);

    byte[] GenerarPlantillaAsignaciones();

    List<long> ParsearNumerosDocumento(Stream stream);

    byte[] GenerarPlantillaReporte();

    List<Application.DTOs.Reportes.FilaReporteRaw> ParsearFilasReporte(Stream stream, string extension);
}

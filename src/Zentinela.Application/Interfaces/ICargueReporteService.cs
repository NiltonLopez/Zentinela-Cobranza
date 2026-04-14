using Zentinela.Application.DTOs.Reportes;

namespace Zentinela.Application.Interfaces;

public interface ICargueReporteService
{
    /// <summary>
    /// Parsea el Excel, inyecta carteraID en cada fila, valida AgenteID contra BD
    /// y determina si los TitularIDs son existentes o nuevos (con Nombre para crear).
    /// Devuelve preview con primeros 10 registros, conteos y filas válidas completas.
    /// </summary>
    Task<PreviewReporteDto> ValidarExcelAsync(Stream excelStream, int carteraID);

    /// <summary>
    /// Carga las filas válidas en ReporteEstadoAuxiliar y ejecuta:
    /// sp_ProcesarUltimoEstado (titular nuevo + UPSERT) y sp_ProcesarHistoricoEstado (INSERT append).
    /// </summary>
    Task<ResultadoReporteDto> ConfirmarCargueAsync(List<FilaReporteRaw> filasValidas);

    /// <summary>Genera la plantilla Excel con los campos del reporte (sin CarteraID — viene de la UI).</summary>
    byte[] GenerarPlantilla();
}

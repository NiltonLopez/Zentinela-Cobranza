using Zentinela.Application.DTOs.Reportes;

namespace Zentinela.Application.Interfaces;

public interface ICargueReporteService
{
    /// <summary>
    /// Parsea el archivo (CSV o XLSX), inyecta carteraID en cada fila, valida AgenteID contra BD
    /// y determina si los TitularIDs son existentes o nuevos (con Nombre para crear).
    /// Devuelve preview con primeros 10 registros, conteos y filas válidas completas.
    /// </summary>
    Task<PreviewReporteDto> ValidarArchivoAsync(Stream archivoStream, int carteraID, string extension);

    /// <summary>
    /// SqlBulkCopy a tabla temporal → MERGE a UltimoEstadoTitular (UPSERT)
    /// + INSERT a HistoricoEstadoTitular (append donde no exista la PK).
    /// Todo en una transacción. Soporta 900K+ filas.
    /// </summary>
    Task<ResultadoReporteDto> ConfirmarCargueAsync(List<FilaReporteRaw> filasValidas);

    /// <summary>Genera la plantilla Excel con los campos del reporte (sin CarteraID — viene de la UI).</summary>
    byte[] GenerarPlantilla();

    /// <summary>
    /// Devuelve la fecha del último registro procesado en UltimoEstadoTitular para la cartera.
    /// Null si la cartera no tiene registros aún.
    /// </summary>
    Task<DateTime?> ObtenerUltimaActualizacionAsync(int carteraID);
}

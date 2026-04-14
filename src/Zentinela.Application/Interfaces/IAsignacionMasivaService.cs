using Zentinela.Application.DTOs.Asignaciones;

namespace Zentinela.Application.Interfaces;

public interface IAsignacionMasivaService
{
    /// <summary>
    /// Parsea el Excel, valida cada NumeroDocumento contra la BD y devuelve
    /// una vista previa con los primeros 10 registros más los conteos totales.
    /// </summary>
    Task<PreviewAsignacionDto> ValidarExcelAsync(
        Stream excelStream, int carteraID, int liderID);

    /// <summary>
    /// Inserta los titularIDs en Titular_CarteraAuxiliar y ejecuta
    /// sp_ProcesarAsignacionCartera para transferirlos a Titular_Cartera.
    /// </summary>
    Task<ResultadoCargueDto> ConfirmarCargueAsync(
        int carteraID, int liderID, List<long> titularIDs);

    /// <summary>Genera la plantilla Excel vacía lista para que el admin la rellene.</summary>
    byte[] GenerarPlantilla();
}

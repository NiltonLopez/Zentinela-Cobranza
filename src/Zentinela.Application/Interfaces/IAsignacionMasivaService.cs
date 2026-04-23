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
    /// Crea los titulares nuevos en Titular, luego inserta todos los IDs en
    /// Titular_CarteraAuxiliar y ejecuta sp_ProcesarAsignacionCartera.
    /// </summary>
    Task<ResultadoCargueDto> ConfirmarCargueAsync(
        int carteraID, int liderID, List<long> titularIDs, List<NuevoTitularDto> nuevosTitulares);

    /// <summary>Genera la plantilla Excel vacía lista para que el admin la rellene.</summary>
    byte[] GenerarPlantilla();
}

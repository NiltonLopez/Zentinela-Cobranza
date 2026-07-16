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
    /// Variante "Cartera Completo": el Excel trae una columna LiderID obligatoria
    /// por fila. Valida que cada LiderID pertenezca a un líder con relación activa
    /// en la cartera, y devuelve los pares (TitularID, LiderID) en AsignacionesValidas.
    /// </summary>
    Task<PreviewAsignacionDto> ValidarExcelCompletoAsync(
        Stream excelStream, int carteraID);

    /// <summary>
    /// Reemplazo por líder: crea los titulares nuevos en Titular, borra las
    /// asignaciones actuales de (carteraID, liderID) y hace bulk insert de titularIDs.
    /// </summary>
    Task<ResultadoCargueDto> ConfirmarCargueAsync(
        int carteraID, int liderID, List<long> titularIDs, List<NuevoTitularDto> nuevosTitulares);

    /// <summary>
    /// Reemplazo de cartera completa: crea titulares nuevos, borra TODAS las
    /// asignaciones de la cartera (todos los líderes) y hace bulk insert de los
    /// pares (TitularID, LiderID) del archivo.
    /// </summary>
    Task<ResultadoCargueDto> ConfirmarCargueCompletoAsync(
        int carteraID, List<AsignacionValidaDto> asignaciones, List<NuevoTitularDto> nuevosTitulares);

    /// <summary>Genera la plantilla Excel vacía lista para que el admin la rellene.</summary>
    byte[] GenerarPlantilla();

    /// <summary>Plantilla "Cartera Completo" — incluye la columna LiderID obligatoria.</summary>
    byte[] GenerarPlantillaCompleto();
}

using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;

namespace Zentinela.Application.Interfaces;

public interface ICreadorZentinelaService
{
    /// <summary>Retorna el catálogo estático de columnas disponibles en la Base Madre.</summary>
    IEnumerable<ColumnaBaseMadre> ObtenerColumnas();

    /// <summary>
    /// Opciones del dropdown superior. Para Líder: una entrada por cartera asignada.
    /// Para Admin/Director: cada (Cartera × Líder) presente en Lider_Cartera más una
    /// entrada agregada "Cartera Completo" por cartera (LiderID = null).
    /// </summary>
    Task<IEnumerable<OpcionCarteraDto>> ObtenerOpcionesAsync(ContextoUsuario contexto);

    /// <summary>
    /// Retorna las primeras 100 filas que cumplen los filtros, más el total real.
    /// </summary>
    Task<PreviewBaseMadreResponse> ObtenerPreviewAsync(
        ContextoUsuario contexto,
        FiltroBaseMadreRequest filtro);

    /// <summary>
    /// Genera el Excel completo (sin límite de filas) con las columnas seleccionadas.
    /// Retorna los bytes listos para enviar como descarga.
    /// </summary>
    Task<(byte[] Bytes, string NombreArchivo)> ExportarExcelAsync(
        ContextoUsuario contexto,
        FiltroBaseMadreRequest filtro);
}

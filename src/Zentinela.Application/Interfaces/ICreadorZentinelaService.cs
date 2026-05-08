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

    /// <summary>
    /// Escribe el resultado completo (sin límite) como CSV streaming directamente
    /// al <paramref name="output"/> (típicamente HttpContext.Response.Body). No
    /// materializa la cartera en memoria — usa SqlDataReader fila por fila. Es el
    /// camino recomendado para carteras grandes (≥100k filas).
    /// </summary>
    Task EscribirCsvAsync(
        ContextoUsuario contexto,
        FiltroBaseMadreRequest filtro,
        Stream output,
        CancellationToken ct = default);

    /// <summary>
    /// Resuelve el nombre de archivo (BaseMadre_<NombreCartera>_<fecha>.<ext>)
    /// que debe usarse para la descarga. Se necesita poder llamar antes de
    /// empezar a escribir el body (Content-Disposition es header).
    /// </summary>
    Task<string> ResolverNombreArchivoAsync(int carteraID, string extension);
}

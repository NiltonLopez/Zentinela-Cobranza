using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface ICalificadorCarteraService
{
    /// <summary>
    /// Lista de combinaciones Cartera+Líder disponibles según el rol del usuario.
    /// Líder → solo sus asignaciones. Director/Admin → todas.
    /// </summary>
    Task<IEnumerable<CarteraLiderDropdownDto>> ObtenerOpcionesAsync(ContextoUsuario contexto);

    /// <summary>
    /// Calcula todas las métricas para una asignación Cartera+Líder específica,
    /// o para una "Cartera Completa" (todos los líderes de la cartera) cuando
    /// <paramref name="liderID"/> es null.
    /// </summary>
    Task<CalificadorCarteraDto> CalificarAsync(int carteraID, int? liderID);

    /// <summary>
    /// Stream CSV con (Cédula, Nombre, Última Consulta) de los titulares de la
    /// cartera (filtrada por líder si aplica) que clasifican en la categoría
    /// indicada para la fuente de localización dada. Permite descargar la lista
    /// detrás de las cifras de la tabla "Porcentajes de Localización".
    /// </summary>
    /// <param name="fuenteSlug">Slug de la fuente: cajacopi, camara, salud-total, fosyga, compensados, positiva, tigo, sos, claro, mutualser, sura, positiva-empresa, rues, simit, rgm.</param>
    /// <param name="categoria">encontrado | no-encontrado | falta.</param>
    Task EscribirLocalizacionCsvAsync(
        int carteraID, int? liderID,
        string fuenteSlug, string categoria,
        Stream output, CancellationToken ct = default);

    /// <summary>
    /// Nombre sugerido para el archivo CSV de localización
    /// (incluye cartera, líder si aplica, fuente, categoría y fecha).
    /// </summary>
    Task<string> ResolverNombreArchivoLocalizacionAsync(
        int carteraID, int? liderID, string fuenteSlug, string categoria);
}

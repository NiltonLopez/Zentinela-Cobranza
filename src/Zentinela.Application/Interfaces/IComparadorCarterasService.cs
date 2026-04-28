using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface IComparadorCarterasService
{
    /// <summary>
    /// Carteras disponibles en el dropdown según el rol del usuario.
    /// Líder → solo las suyas. Director/Admin → todas las activas.
    /// </summary>
    Task<IEnumerable<CarteraDropdownDto>> ObtenerOpcionesAsync(ContextoUsuario contexto);

    /// <summary>
    /// Compara titular a titular la cartera seleccionada contra todas las demás.
    /// Devuelve una sección por cada cartera que tenga titulares con mejor estado,
    /// con vista previa de hasta 5 registros.
    /// </summary>
    Task<ComparadorCarterasDto> CompararAsync(int carteraID);

    /// <summary>
    /// Lista completa (sin límite) de titulares coincidentes entre carteraID y otraCarteraID,
    /// para generar el Excel individual de esa sección.
    /// </summary>
    Task<IEnumerable<TitularComparativoDto>> ObtenerParaExcelAsync(int carteraID, int otraCarteraID);

    /// <summary>
    /// Lista completa de TODAS las coincidencias de carteraID contra todas las carteras,
    /// agrupadas e identificadas con NombreOtraCartera para el Excel completo.
    /// </summary>
    Task<IEnumerable<TitularComparativoDto>> ObtenerTodosParaExcelAsync(int carteraID);
}

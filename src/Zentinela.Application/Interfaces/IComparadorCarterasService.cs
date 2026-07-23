using Zentinela.Application.DTOs.CreadorZentinela;
using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface IComparadorCarterasService
{
    /// <summary>
    /// Carteras disponibles en el dropdown según el rol del usuario, con la misma
    /// discriminación que el Creador Zentinela: Líder → una entrada por cartera propia.
    /// Admin/Director → una entrada por combinación (Cartera, Líder) más una entrada
    /// agregada "Cartera Completo" (LiderID = null) por cada cartera.
    /// </summary>
    Task<IEnumerable<OpcionCarteraDto>> ObtenerOpcionesAsync(ContextoUsuario contexto);

    /// <summary>Nombre de una cartera por ID, para encabezados y nombres de archivo.</summary>
    Task<string> ObtenerNombreCarteraAsync(int carteraID);

    /// <summary>
    /// Compara titular a titular la cartera seleccionada contra todas las demás.
    /// Devuelve una sección por cada cartera que tenga titulares con mejor estado,
    /// con vista previa de hasta 5 registros. liderID null = cartera completa;
    /// con valor, restringe la base de comparación a las asignaciones de ese líder.
    /// </summary>
    Task<ComparadorCarterasDto> CompararAsync(int carteraID, int? liderID);

    /// <summary>
    /// Lista completa (sin límite) de titulares coincidentes entre carteraID y otraCarteraID,
    /// para generar el Excel individual de esa sección.
    /// </summary>
    Task<IEnumerable<TitularComparativoDto>> ObtenerParaExcelAsync(int carteraID, int? liderID, int otraCarteraID);

    /// <summary>
    /// Lista completa de TODAS las coincidencias de carteraID contra todas las carteras,
    /// agrupadas e identificadas con NombreOtraCartera para el Excel completo.
    /// </summary>
    Task<IEnumerable<TitularComparativoDto>> ObtenerTodosParaExcelAsync(int carteraID, int? liderID);
}

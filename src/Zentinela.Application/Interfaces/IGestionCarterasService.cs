using Zentinela.Application.DTOs.Carteras;

namespace Zentinela.Application.Interfaces;

public interface IGestionCarterasService
{
    /// <summary>Lista todas las relaciones Líder_Cartera con el conteo de titulares asignados.</summary>
    Task<IEnumerable<RelacionLiderCarteraDto>> ListarRelacionesAsync();

    /// <summary>Lista todos los líderes activos para el dropdown de creación.</summary>
    Task<IEnumerable<LiderDropdownDto>> ListarLideresAsync();

    /// <summary>Busca carteras por nombre (máximo 10 resultados) para el autocompletado.</summary>
    Task<IEnumerable<CarteraDto>> BuscarCarterasAsync(string q);

    /// <summary>
    /// Crea una relación Líder_Cartera. Si CarteraID es null crea la cartera primero.
    /// Lanza InvalidOperationException si la relación ya existe.
    /// </summary>
    Task<RelacionLiderCarteraDto> CrearRelacionAsync(CrearRelacionDto dto);

    /// <summary>
    /// Elimina una relación Líder_Cartera.
    /// No elimina los registros de Titular_Cartera — deben reasignarse manualmente.
    /// </summary>
    Task EliminarRelacionAsync(int liderID, int carteraID);
}

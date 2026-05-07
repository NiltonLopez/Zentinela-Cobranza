using Zentinela.Core.Entities;

namespace Zentinela.Core.Interfaces;

public interface ICarteraRepository
{
    Task<IEnumerable<Cartera>> ObtenerPorLiderAsync(int liderID);
    Task<IEnumerable<Cartera>> ObtenerTodasAsync();

    /// <summary>
    /// Devuelve todas las filas de Lider_Cartera con Cartera y Líder hidratados,
    /// ordenadas por NombreCartera y luego por nombre del líder. Solo carteras
    /// activas. Usado para construir el dropdown del Creador Zentinela cuando
    /// el rol es Admin/Director.
    /// </summary>
    Task<IEnumerable<LiderCartera>> ObtenerCombinacionesActivasAsync();
}

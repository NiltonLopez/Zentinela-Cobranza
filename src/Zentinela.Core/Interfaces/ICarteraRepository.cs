using Zentinela.Core.Entities;

namespace Zentinela.Core.Interfaces;

public interface ICarteraRepository
{
    Task<IEnumerable<Cartera>> ObtenerPorLiderAsync(int liderID);
    Task<IEnumerable<Cartera>> ObtenerTodasAsync();
}

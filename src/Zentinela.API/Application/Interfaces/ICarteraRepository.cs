using Zentinela.API.Domain.Entities;

namespace Zentinela.API.Application.Interfaces;

public interface ICarteraRepository
{
    Task<IEnumerable<Cartera>> ObtenerPorLiderAsync(int liderID);
    Task<IEnumerable<Cartera>> ObtenerTodasAsync();
}

using Zentinela.Application.DTOs.Virtualidad.PagosVsImpacto;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadPagosImpactoService
{
    Task<PagosImpactoDto> GetInformeAsync(DateOnly desde, DateOnly hasta, int? carteraID);
}

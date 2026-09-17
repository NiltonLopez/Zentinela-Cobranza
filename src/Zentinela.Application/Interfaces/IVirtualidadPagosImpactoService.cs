using Zentinela.Application.DTOs.Virtualidad.PagosVsImpacto;

namespace Zentinela.Application.Interfaces;

public interface IVirtualidadPagosImpactoService
{
    /// <param name="liderID">Opcional. Acota los pagos a los registrados a nombre de ese líder.</param>
    /// <param name="carteraID">Opcional. Acota los pagos a esa cartera.</param>
    Task<PagosImpactoDto> GetInformeAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}

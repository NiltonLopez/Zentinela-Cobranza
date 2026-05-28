using Zentinela.Application.DTOs.Dashboard;

namespace Zentinela.Application.Interfaces;

public interface IDashboardDirectorService
{
    Task<List<LiderDropdownDto>>          ObtenerLideresAsync();
    Task<List<CarteraDropdownDto>>        ObtenerCarterasLiderAsync(int liderID);
    Task<List<CarteraDropdownDto>>        ObtenerCarterasAsync();
    Task<List<LiderCarteraVinculoDto>>    ObtenerVinculosLiderCarteraAsync();
    Task<DashboardDirectorDto>     ObtenerDashboardAsync(int? liderID, int? carteraID, DateOnly? fechaDesde, DateOnly? fechaHasta);
    Task<List<PagoDirectorDto>>    ObtenerPagosAsync(int? liderID, int? carteraID, DateOnly? fechaDesde, DateOnly? fechaHasta);
}

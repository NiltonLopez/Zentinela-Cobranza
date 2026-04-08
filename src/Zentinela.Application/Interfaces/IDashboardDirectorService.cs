using Zentinela.Application.DTOs.Dashboard;

namespace Zentinela.Application.Interfaces;

public interface IDashboardDirectorService
{
    Task<List<LiderDropdownDto>>   ObtenerLideresAsync();
    Task<List<CarteraDropdownDto>> ObtenerCarterasLiderAsync(int liderID);
    Task<DashboardDirectorDto>     ObtenerDashboardAsync(int? liderID, int? carteraID);
}

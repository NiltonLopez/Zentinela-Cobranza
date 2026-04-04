using Zentinela.Application.DTOs.Dashboard;

namespace Zentinela.Application.Interfaces;

public interface IDashboardAsesorService
{
    Task<DashboardAsesorDto> ObtenerDashboardAsync(long numeroDocumento);
}

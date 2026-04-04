using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Asesor)]
public class DashboardController : ControllerBase
{
    private readonly IDashboardAsesorService _dashboardService;

    public DashboardController(IDashboardAsesorService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Retorna el dashboard completo del asesor autenticado:
    /// supervisor, acuerdos vigentes con semáforo y métricas de rendimiento.
    /// </summary>
    [HttpGet("asesor")]
    public async Task<IActionResult> ObtenerDashboardAsesor()
    {
        var numDocClaim = User.FindFirst("numeroDocumento")?.Value;
        if (!long.TryParse(numDocClaim, out var numeroDocumento) || numeroDocumento == 0)
            return Forbid();

        var resultado = await _dashboardService.ObtenerDashboardAsync(numeroDocumento);
        return Ok(resultado);
    }
}

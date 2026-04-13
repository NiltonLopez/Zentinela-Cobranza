using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardAsesorService   _asesorService;
    private readonly IDashboardDirectorService _directorService;

    public DashboardController(
        IDashboardAsesorService asesorService,
        IDashboardDirectorService directorService)
    {
        _asesorService   = asesorService;
        _directorService = directorService;
    }

    // ── Asesor ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dashboard del asesor autenticado: supervisor, acuerdos vigentes y métricas.
    /// </summary>
    [HttpGet("asesor")]
    [Authorize(Roles = Roles.Asesor)]
    public async Task<IActionResult> ObtenerDashboardAsesor()
    {
        var numDocClaim = User.FindFirst("numeroDocumento")?.Value;
        if (!long.TryParse(numDocClaim, out var numeroDocumento) || numeroDocumento == 0)
            return Forbid();

        var resultado = await _asesorService.ObtenerDashboardAsync(numeroDocumento);
        return Ok(resultado);
    }

    // ── Director ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lista de líderes activos para el dropdown de filtros del director.
    /// </summary>
    [HttpGet("director/lideres")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerLideres()
    {
        var lideres = await _directorService.ObtenerLideresAsync();
        return Ok(lideres);
    }

    /// <summary>
    /// Carteras de un líder específico para el segundo dropdown.
    /// </summary>
    [HttpGet("director/carteras/{liderID:int}")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerCarterasLider(int liderID)
    {
        var carteras = await _directorService.ObtenerCarterasLiderAsync(liderID);
        return Ok(carteras);
    }

    /// <summary>
    /// Dashboard del director.
    /// - Sin parámetros: vista global con desglose por líder.
    /// - Con liderID: métricas del líder con desglose por asesor.
    /// - Con liderID + carteraID: métricas de la cartera con desglose por asesor.
    /// - fechaDesde / fechaHasta: rango de análisis (default: primer día del mes actual → hoy).
    /// </summary>
    [HttpGet("director")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerDashboardDirector(
        [FromQuery] int?      liderID,
        [FromQuery] int?      carteraID,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        var resultado = await _directorService.ObtenerDashboardAsync(liderID, carteraID, fechaDesde, fechaHasta);
        return Ok(resultado);
    }
}

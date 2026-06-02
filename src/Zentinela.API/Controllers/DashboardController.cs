using System.Security.Claims;
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
    public async Task<IActionResult> ObtenerDashboardAsesor(
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        var numDocClaim = User.FindFirst("numeroDocumento")?.Value;
        if (!long.TryParse(numDocClaim, out var numeroDocumento) || numeroDocumento == 0)
            return Forbid();

        try
        {
            var resultado = await _asesorService.ObtenerDashboardAsync(numeroDocumento, fechaDesde, fechaHasta);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = $"Error al cargar el dashboard: {ex.Message}" });
        }
    }

    /// <summary>
    /// Detalle de pagos confirmados del asesor autenticado, con filtro de fechas opcional.
    /// </summary>
    [HttpGet("asesor/pagos")]
    [Authorize(Roles = Roles.Asesor)]
    public async Task<IActionResult> ObtenerPagosAsesor(
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        var numDocClaim = User.FindFirst("numeroDocumento")?.Value;
        if (!long.TryParse(numDocClaim, out var numeroDocumento) || numeroDocumento == 0)
            return Forbid();

        try
        {
            var resultado = await _asesorService.ObtenerPagosDetalleAsync((int)numeroDocumento, fechaDesde, fechaHasta);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = $"Error al cargar los pagos: {ex.Message}" });
        }
    }

    // ── Líder ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Carteras propias del líder autenticado para el dropdown de filtros.
    /// </summary>
    [HttpGet("lider/carteras")]
    [Authorize(Roles = Roles.Lider)]
    public async Task<IActionResult> ObtenerCarterasLiderPropio()
    {
        if (!int.TryParse(User.FindFirstValue("liderID"), out var liderID))
            return Forbid();

        var carteras = await _directorService.ObtenerCarterasLiderAsync(liderID);
        return Ok(carteras);
    }

    /// <summary>
    /// Dashboard del líder autenticado, opcionalmente filtrado por cartera y fechas.
    /// </summary>
    [HttpGet("lider")]
    [Authorize(Roles = Roles.Lider)]
    public async Task<IActionResult> ObtenerDashboardLider(
        [FromQuery] int?      carteraID,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        if (!int.TryParse(User.FindFirstValue("liderID"), out var liderID))
            return Forbid();

        var resultado = await _directorService.ObtenerDashboardAsync(liderID, carteraID, fechaDesde, fechaHasta);
        return Ok(resultado);
    }

    /// <summary>
    /// Pagos confirmados de las carteras del líder autenticado.
    /// </summary>
    [HttpGet("lider/pagos")]
    [Authorize(Roles = Roles.Lider)]
    public async Task<IActionResult> ObtenerPagosLider(
        [FromQuery] int?      carteraID,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        if (!int.TryParse(User.FindFirstValue("liderID"), out var liderID))
            return Forbid();

        try
        {
            var resultado = await _directorService.ObtenerPagosAsync(liderID, carteraID, fechaDesde, fechaHasta);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = $"Error al cargar los pagos: {ex.Message}" });
        }
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
    /// Todas las carteras internas (lista plana) para el dropdown de cartera independiente
    /// del director en Pagos Confirmados. Permite filtrar por "Cartera Completa" (Líder=Todos).
    /// </summary>
    [HttpGet("director/carteras")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerTodasCarteras()
    {
        var carteras = await _directorService.ObtenerCarterasAsync();
        return Ok(carteras);
    }

    /// <summary>
    /// Pares internos líder↔cartera. El frontend de Pagos Confirmados los usa para que los
    /// selectores de líder y cartera se filtren mutuamente (dropdowns dependientes).
    /// </summary>
    [HttpGet("director/vinculos")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerVinculosLiderCartera()
    {
        var vinculos = await _directorService.ObtenerVinculosLiderCarteraAsync();
        return Ok(vinculos);
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

    /// <summary>
    /// Detalle de todos los pagos confirmados en la empresa con filtros opcionales.
    /// </summary>
    [HttpGet("director/pagos")]
    [Authorize(Roles = $"{Roles.Director},{Roles.Admin}")]
    public async Task<IActionResult> ObtenerPagosDirector(
        [FromQuery] int?      liderID,
        [FromQuery] int?      carteraID,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta)
    {
        try
        {
            var resultado = await _directorService.ObtenerPagosAsync(liderID, carteraID, fechaDesde, fechaHasta);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = $"Error al cargar los pagos: {ex.Message}" });
        }
    }
}

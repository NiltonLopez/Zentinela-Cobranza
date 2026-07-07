using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.AuditoriaGestiones;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/informes/auditoria-gestiones")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Director}")]
public class AuditoriaGestionesController : ControllerBase
{
    private readonly IAuditoriaGestionesService _service;

    public AuditoriaGestionesController(IAuditoriaGestionesService service) => _service = service;

    /// <summary>
    /// Audita una o varias carteras consultando HistoricoEstadoTitular por
    /// FechaUltimaGestion entre fechaInicio y fechaFin.
    /// </summary>
    [HttpPost("analizar")]
    public async Task<IActionResult> Analizar([FromBody] FiltroAuditoria filtro, CancellationToken ct)
    {
        if (filtro.Carteras is null || filtro.Carteras.Count == 0)
            return BadRequest(new { mensaje = "Selecciona al menos una cartera." });
        if (filtro.FechaInicio > filtro.FechaFin)
            return BadRequest(new { mensaje = "El rango de fechas es inválido." });

        var resultado = await _service.AnalizarAsync(filtro, ct);
        return Ok(resultado);
    }

    /// <summary>
    /// Estima el costo (COP) de evaluar con IA las gestiones del filtro, sin ejecutar
    /// el modelo. Se usa para mostrar el gasto aproximado tras elegir el rango de fechas.
    /// </summary>
    [HttpPost("estimar-costo")]
    public async Task<IActionResult> EstimarCosto([FromBody] FiltroAuditoria filtro, CancellationToken ct)
    {
        if (filtro.Carteras is null || filtro.Carteras.Count == 0)
            return BadRequest(new { mensaje = "Selecciona al menos una cartera." });
        if (filtro.FechaInicio > filtro.FechaFin)
            return BadRequest(new { mensaje = "El rango de fechas es inválido." });

        var estimacion = await _service.EstimarCostoAsync(filtro, ct);
        return Ok(estimacion);
    }

    /// <summary>
    /// Lista los reportes ya generados y disponibles en memoria (compartidos entre los
    /// perfiles con permiso). Expiran a las 48 h.
    /// </summary>
    [HttpGet("reportes")]
    public IActionResult Reportes() => Ok(_service.ListarReportes());
}

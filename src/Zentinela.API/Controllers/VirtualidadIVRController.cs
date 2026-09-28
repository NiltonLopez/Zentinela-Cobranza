using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informe de IVR (solo lectura). El alta de solicitudes/resultados IVR se hace en
/// el módulo "Gestión de Solicitudes de Virtualidad" (api/virtualidad/solicitudes).
///
/// Un Líder también accede, pero solo a sus propios datos: el <c>liderID</c> se
/// fuerza desde el claim del token (ver <see cref="AlcanceLiderExtensions"/>).
/// </summary>
[ApiController]
[Route("api/virtualidad/ivr")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas},{Roles.Lider}")]
public class VirtualidadIVRController : ControllerBase
{
    private readonly IVirtualidadIVRService _service;

    public VirtualidadIVRController(IVirtualidadIVRService service)
        => _service = service;

    [HttpGet("informe")]
    public async Task<IActionResult> GetInforme(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] int? liderID,
        [FromQuery] int? carteraID)
    {
        if (!User.TryResolverLiderID(liderID, out var liderEfectivo))
            return Forbid();

        var resultado = await _service.GetInformeAsync(desde, hasta, liderEfectivo, carteraID);
        return Ok(resultado);
    }

    /// <summary>Análisis de embudo y desenlaces del IVR sobre clientes únicos (ADR-031).</summary>
    [HttpGet("analisis")]
    public async Task<IActionResult> GetAnalisis(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] int? liderID,
        [FromQuery] int? carteraID)
    {
        if (hasta < desde)
            return BadRequest(new { mensaje = "La fecha final no puede ser anterior a la inicial." });

        if (!User.TryResolverLiderID(liderID, out var liderEfectivo))
            return Forbid();

        return Ok(await _service.GetAnalisisAsync(desde, hasta, liderEfectivo, carteraID));
    }

    /// <summary>
    /// Excel del informe (hoja Resumen + hoja Detalle por cliente) para que el líder trabaje
    /// sus estrategias. Mismo alcance que el análisis: un Líder solo baja sus datos.
    /// </summary>
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] int? liderID,
        [FromQuery] int? carteraID)
    {
        if (hasta < desde)
            return BadRequest(new { mensaje = "La fecha final no puede ser anterior a la inicial." });

        if (!User.TryResolverLiderID(liderID, out var liderEfectivo))
            return Forbid();

        var archivo = await _service.ExportarExcelAsync(desde, hasta, liderEfectivo, carteraID);
        return File(archivo,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Informe_IVR_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx");
    }
}

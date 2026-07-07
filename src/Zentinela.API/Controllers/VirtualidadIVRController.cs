using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informe de IVR (solo lectura). El alta de solicitudes/resultados IVR se hace en
/// el módulo "Gestión de Solicitudes de Virtualidad" (api/virtualidad/solicitudes).
/// </summary>
[ApiController]
[Route("api/virtualidad/ivr")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
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
        var resultado = await _service.GetInformeAsync(desde, hasta, liderID, carteraID);
        return Ok(resultado);
    }
}

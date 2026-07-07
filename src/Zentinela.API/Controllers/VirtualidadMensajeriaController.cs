using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informe de Mensajería/SMS (solo lectura). El alta de solicitudes/resultados se
/// hace en el módulo "Gestión de Solicitudes de Virtualidad" (api/virtualidad/solicitudes).
/// </summary>
[ApiController]
[Route("api/virtualidad/mensajeria")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class VirtualidadMensajeriaController : ControllerBase
{
    private readonly IVirtualidadMensajeriaService _service;

    public VirtualidadMensajeriaController(IVirtualidadMensajeriaService service)
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

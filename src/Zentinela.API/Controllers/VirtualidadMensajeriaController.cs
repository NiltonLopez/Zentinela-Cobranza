using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informe de Mensajería/SMS (solo lectura). El alta de solicitudes/resultados se
/// hace en el módulo "Gestión de Solicitudes de Virtualidad" (api/virtualidad/solicitudes).
///
/// Un Líder también accede, pero solo a sus propios datos: el <c>liderID</c> se
/// fuerza desde el claim del token (ver <see cref="AlcanceLiderExtensions"/>).
/// </summary>
[ApiController]
[Route("api/virtualidad/mensajeria")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas},{Roles.Lider}")]
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
        if (!User.TryResolverLiderID(liderID, out var liderEfectivo))
            return Forbid();

        var resultado = await _service.GetInformeAsync(desde, hasta, liderEfectivo, carteraID);
        return Ok(resultado);
    }
}

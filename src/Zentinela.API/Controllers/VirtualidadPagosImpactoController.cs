using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Pagos vs canal de impacto (solo lectura).
///
/// Un Líder también accede, pero solo a sus propios pagos: el <c>liderID</c> se
/// fuerza desde el claim del token (ver <see cref="AlcanceLiderExtensions"/>).
/// </summary>
[ApiController]
[Route("api/virtualidad/pagos-impacto")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas},{Roles.Lider}")]
public class VirtualidadPagosImpactoController : ControllerBase
{
    private readonly IVirtualidadPagosImpactoService _service;

    public VirtualidadPagosImpactoController(IVirtualidadPagosImpactoService service)
        => _service = service;

    [HttpGet("informe")]
    public async Task<IActionResult> GetInforme(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] int?     liderID,
        [FromQuery] int?     carteraID)
    {
        if (!User.TryResolverLiderID(liderID, out var liderEfectivo))
            return Forbid();

        return Ok(await _service.GetInformeAsync(desde, hasta, liderEfectivo, carteraID));
    }
}

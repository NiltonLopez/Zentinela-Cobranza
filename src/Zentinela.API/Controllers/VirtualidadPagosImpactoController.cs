using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/virtualidad/pagos-impacto")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class VirtualidadPagosImpactoController : ControllerBase
{
    private readonly IVirtualidadPagosImpactoService _service;

    public VirtualidadPagosImpactoController(IVirtualidadPagosImpactoService service)
        => _service = service;

    [HttpGet("informe")]
    public async Task<IActionResult> GetInforme(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] int?     carteraID)
    {
        return Ok(await _service.GetInformeAsync(desde, hasta, carteraID));
    }
}

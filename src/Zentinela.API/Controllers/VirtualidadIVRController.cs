using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.IVR;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/virtualidad/ivr")]
[Authorize(Roles = Roles.Admin)]
public class VirtualidadIVRController : ControllerBase
{
    private readonly IVirtualidadIVRService _service;

    public VirtualidadIVRController(IVirtualidadIVRService service)
        => _service = service;

    [HttpPost("pre-envio/validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ValidarPreEnvio([FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        return Ok(await _service.ValidarPreEnvioAsync(stream));
    }

    [HttpPost("pre-envio/confirmar")]
    public async Task<IActionResult> ConfirmarPreEnvio([FromBody] ConfirmarIVRRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var insertados = await _service.ConfirmarPreEnvioAsync(request.FilasValidas);
        return Ok(new { insertados, mensaje = $"Se registraron {insertados} IVR pre-envío." });
    }

    [HttpPost("post-envio/validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ValidarPostEnvio([FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        return Ok(await _service.ValidarPostEnvioAsync(stream));
    }

    [HttpPost("post-envio/confirmar")]
    public async Task<IActionResult> ConfirmarPostEnvio([FromBody] ConfirmarIVRRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var actualizados = await _service.ConfirmarPostEnvioAsync(request.FilasValidas);
        return Ok(new { actualizados, mensaje = $"Se actualizaron {actualizados} registros con resultado." });
    }

    [HttpGet("informe")]
    public async Task<IActionResult> GetInforme(
        [FromQuery] int?     carteraID,
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta)
    {
        return Ok(await _service.GetInformeAsync(carteraID, desde, hasta));
    }
}

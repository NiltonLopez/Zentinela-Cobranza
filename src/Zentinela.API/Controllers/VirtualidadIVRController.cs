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

    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla()
    {
        var bytes = _service.GenerarPlantilla();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla_ivr.xlsx");
    }

    [HttpPost("validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Validar([FromForm] IFormFile archivo, [FromForm] int carteraID)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });
        if (carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });

        using var stream = archivo.OpenReadStream();
        var resultado    = await _service.ValidarExcelAsync(stream, carteraID);
        return Ok(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarIVRRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var insertados = await _service.ConfirmarAsync(request.FilasValidas);
        return Ok(new { insertados, mensaje = $"Se registraron {insertados} envíos IVR correctamente." });
    }

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

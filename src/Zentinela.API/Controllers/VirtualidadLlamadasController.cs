using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.LlamadasEntrantes;
using Zentinela.Application.DTOs.Virtualidad.LlamadasPerdidas;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/virtualidad/llamadas")]
[Authorize(Roles = Roles.Admin)]
public class VirtualidadLlamadasController : ControllerBase
{
    private readonly IVirtualidadLlamadasService  _perdidas;
    private readonly IVirtualidadEntrantesService _entrantes;

    public VirtualidadLlamadasController(
        IVirtualidadLlamadasService  perdidas,
        IVirtualidadEntrantesService entrantes)
    {
        _perdidas  = perdidas;
        _entrantes = entrantes;
    }

    // ── Llamadas perdidas ─────────────────────────────────────────────────────

    [HttpPost("perdidas/validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ValidarPerdidas([FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        return Ok(await _perdidas.ValidarExcelAsync(stream));
    }

    [HttpPost("perdidas/confirmar")]
    public async Task<IActionResult> ConfirmarPerdidas([FromBody] ConfirmarLlamadasRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var insertados = await _perdidas.ConfirmarAsync(request.FilasValidas);
        return Ok(new { insertados, mensaje = $"Se registraron {insertados} llamadas perdidas." });
    }

    [HttpGet("perdidas/informe")]
    public async Task<IActionResult> GetInformePerdidas()
        => Ok(await _perdidas.GetInformeAsync());

    // ── Llamadas entrantes ────────────────────────────────────────────────────

    [HttpPost("entrantes/validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ValidarEntrantes([FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        return Ok(await _entrantes.ValidarExcelAsync(stream));
    }

    [HttpPost("entrantes/confirmar")]
    public async Task<IActionResult> ConfirmarEntrantes([FromBody] ConfirmarEntrantesRequest request)
    {
        if (request.FilasParaGuardar.Count == 0)
            return BadRequest(new { mensaje = "No hay filas para guardar." });

        var insertados = await _entrantes.ConfirmarAsync(request.FilasParaGuardar);
        return Ok(new { insertados, mensaje = $"Se registraron {insertados} llamadas entrantes." });
    }

    [HttpGet("entrantes/informe")]
    public async Task<IActionResult> GetInformeEntrantes([FromQuery] int anio)
    {
        if (anio < 2020 || anio > 2099)
            anio = DateTime.Today.Year;
        return Ok(await _entrantes.GetInformeAsync(anio));
    }
}

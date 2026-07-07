using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.Solicitudes;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Módulo "Gestión de Solicitudes de Virtualidad" (Administración).
/// Canales soportados: IVR | SMS | Correo. Fase Solicitud (Estado null) +
/// fase Resultado (actualiza Estado por ID).
/// </summary>
[ApiController]
[Route("api/virtualidad/solicitudes")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class SolicitudesVirtualidadController : ControllerBase
{
    private readonly ISolicitudVirtualidadService _service;

    public SolicitudesVirtualidadController(ISolicitudVirtualidadService service)
        => _service = service;

    private bool CanalValido(string? canal) =>
        canal is "IVR" or "SMS" or "Correo";

    // ── Fase Solicitud ──────────────────────────────────────────────────────

    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla([FromQuery] string canal)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });

        var bytes = _service.GenerarPlantilla(canal);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"plantilla_solicitud_{canal.ToLowerInvariant()}.xlsx");
    }

    [HttpPost("validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Validar([FromForm] IFormFile archivo, [FromForm] string canal)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        var resultado    = await _service.ValidarAsync(stream, canal);
        return Ok(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarSolicitudRequest request)
    {
        if (!CanalValido(request.Canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var insertados = await _service.ConfirmarSolicitudAsync(request.FilasValidas, request.Canal);
        return Ok(new { insertados, mensaje = $"Se registraron {insertados} solicitudes de {request.Canal} correctamente." });
    }

    // ── Fase Resultado ──────────────────────────────────────────────────────

    [HttpGet("pendientes")]
    public async Task<IActionResult> DescargarPendientes(
        [FromQuery] string canal,
        [FromQuery] int? carteraID,
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });

        var bytes = await _service.DescargarPendientesAsync(canal, carteraID, desde, hasta);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"pendientes_{canal.ToLowerInvariant()}.xlsx");
    }

    [HttpPost("resultado/validar")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ValidarResultado([FromForm] IFormFile archivo, [FromForm] string canal)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo Excel (.xlsx)." });
        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Solo se aceptan archivos .xlsx." });

        using var stream = archivo.OpenReadStream();
        var resultado    = await _service.ValidarResultadoAsync(stream, canal);
        return Ok(resultado);
    }

    [HttpPost("resultado/confirmar")]
    public async Task<IActionResult> ConfirmarResultado([FromBody] ConfirmarResultadoRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas válidas para confirmar." });

        var actualizados = await _service.ConfirmarResultadoAsync(request.FilasValidas);
        return Ok(new { actualizados, mensaje = $"Se actualizaron {actualizados} resultados correctamente." });
    }
}

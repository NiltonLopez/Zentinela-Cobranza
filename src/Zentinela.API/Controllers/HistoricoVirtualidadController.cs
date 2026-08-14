using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Virtualidad.Historico;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Services;

namespace Zentinela.API.Controllers;

/// <summary>
/// Módulo "Gestión de Histórico de Virtualidad" (Administración).
/// Dos formas de carga según el canal (ADR-031):
///   - IVR: carga única del archivo del marcador, que ya trae el resultado.
///   - SMS / Correo: fase Solicitud (Estado null) + fase Resultado (actualiza Estado por ID).
/// </summary>
[ApiController]
[Route("api/virtualidad/historico")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class HistoricoVirtualidadController : ControllerBase
{
    private const long MaxBytes = 50L * 1024 * 1024;

    private readonly IHistoricoVirtualidadService _service;

    public HistoricoVirtualidadController(IHistoricoVirtualidadService service)
        => _service = service;

    private static bool CanalValido(string? canal) => CatalogoVirtualidad.EsCanalValido(canal);

    /// <summary>
    /// Los endpoints de dos fases no aplican a IVR. Se rechazan explícitamente para que un
    /// cliente desactualizado no siga insertando IVR por el camino equivocado.
    /// </summary>
    private IActionResult? RechazarSiEsIvr(string? canal) =>
        CatalogoVirtualidad.EsIvr(canal)
            ? BadRequest(new
            {
                mensaje = "El canal IVR usa carga única de resultados. Use /api/virtualidad/historico/ivr/validar.",
            })
            : null;

    private IActionResult? ValidarArchivo(IFormFile? archivo, params string[] extensiones)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = $"Debe adjuntar un archivo ({string.Join(" o ", extensiones)})." });

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        return extensiones.Contains(ext)
            ? null
            : BadRequest(new { mensaje = $"Solo se aceptan archivos {string.Join(" o ", extensiones)}." });
    }

    // ── Catálogo ────────────────────────────────────────────────────────────

    /// <summary>Modo de carga, estados y columnas del canal. Evita duplicar el catálogo en el front.</summary>
    [HttpGet("catalogo")]
    public IActionResult ObtenerCatalogo([FromQuery] string canal)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });

        return Ok(_service.ObtenerCatalogo(canal));
    }

    // ── Carga única — canal IVR ─────────────────────────────────────────────

    [HttpPost("ivr/validar")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> ValidarCargaIvr([FromForm] IFormFile archivo)
    {
        if (ValidarArchivo(archivo, ".csv", ".xlsx") is { } error) return error;

        try
        {
            using var stream = archivo.OpenReadStream();
            return Ok(await _service.ValidarCargaIvrAsync(stream, archivo.FileName));
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensaje = $"Error al procesar el archivo: {ex.Message}" });
        }
    }

    [HttpPost("ivr/confirmar")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> ConfirmarCargaIvr(
        [FromForm] IFormFile archivo, [FromForm] string huella)
    {
        if (ValidarArchivo(archivo, ".csv", ".xlsx") is { } error) return error;
        if (string.IsNullOrWhiteSpace(huella))
            return Conflict(new { mensaje = "Falta la huella del archivo validado. Valida el archivo antes de confirmar." });

        try
        {
            using var stream = archivo.OpenReadStream();
            return Ok(await _service.ConfirmarCargaIvrAsync(stream, archivo.FileName, huella));
        }
        catch (CargaIvrConflictoException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensaje = $"Error al cargar el archivo: {ex.Message}" });
        }
    }

    // ── Fase Solicitud (SMS / Correo) ───────────────────────────────────────

    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla([FromQuery] string canal)
    {
        if (!CanalValido(canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });
        if (RechazarSiEsIvr(canal) is { } noIvr) return noIvr;

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
        if (RechazarSiEsIvr(canal) is { } noIvr) return noIvr;
        if (ValidarArchivo(archivo, ".xlsx") is { } error) return error;

        using var stream = archivo.OpenReadStream();
        var resultado    = await _service.ValidarAsync(stream, canal);
        return Ok(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarSolicitudRequest request)
    {
        if (!CanalValido(request.Canal))
            return BadRequest(new { mensaje = "Canal inválido. Use IVR, SMS o Correo." });
        if (RechazarSiEsIvr(request.Canal) is { } noIvr) return noIvr;
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
        if (RechazarSiEsIvr(canal) is { } noIvr) return noIvr;

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
        if (RechazarSiEsIvr(canal) is { } noIvr) return noIvr;
        if (ValidarArchivo(archivo, ".xlsx") is { } error) return error;

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

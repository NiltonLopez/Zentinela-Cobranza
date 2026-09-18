using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.DTOs.CreadorVirtualidad;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Cartera → Creador de Virtualidad (ADR-037). Genera el archivo que se entrega al área
/// de virtualidad con un mensaje del catálogo autorizado; el líder nunca redacta.
///
/// Alcance por rol: un Líder solo genera para sí (el liderID de la petición se ignora y se
/// usa el del token); Director y Admin deben indicar para qué líder.
/// </summary>
[ApiController]
[Route("api/creador-virtualidad")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Director},{Roles.Lider}")]
public class CreadorVirtualidadController : ControllerBase
{
    private const long MaxBytes = 50L * 1024 * 1024;
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ICreadorVirtualidadSmsService _sms;

    public CreadorVirtualidadController(ICreadorVirtualidadSmsService sms) => _sms = sms;

    // ── Alcance ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve el error a responder, o null y el líder efectivo. Para Líder es el del
    /// claim (403 si falta); para Director/Admin es obligatorio elegir uno (400 si no).
    /// </summary>
    private IActionResult? ResolverLider(int? liderID, out int efectivo)
    {
        efectivo = 0;
        if (!User.TryResolverLiderID(liderID, out var resuelto)) return Forbid();
        if (resuelto is null) return BadRequest(new { mensaje = "Selecciona el líder para el que se genera la estrategia." });
        efectivo = resuelto.Value;
        return null;
    }

    private string Usuario => User.FindFirstValue("userName") ?? User.Identity?.Name ?? "desconocido";

    private IActionResult? ValidarArchivo(IFormFile? archivo, string nombreCampo, params string[] extensiones)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = $"Debe adjuntar {nombreCampo} ({string.Join(" o ", extensiones)})." });
        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        return extensiones.Contains(ext)
            ? null
            : BadRequest(new { mensaje = $"{nombreCampo}: solo se aceptan archivos {string.Join(" o ", extensiones)}." });
    }

    private static EntradaSms Entrada(
        IFormFile archivo, Stream stream, int liderID, List<string>? carteras,
        IFormFile? listaNegra, Stream? streamListaNegra) =>
        new(stream, archivo.FileName, liderID,
            carteras?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList() ?? [],
            streamListaNegra, listaNegra?.FileName);

    // ── Catálogos de apoyo ──────────────────────────────────────────────────

    [HttpGet("sms/plantilla")]
    public IActionResult DescargarPlantilla() =>
        File(_sms.GenerarPlantilla(), Xlsx, "plantilla_sms.xlsx");

    /// <summary>Carteras presentes en ListaNegraTelefonos; el usuario elige cuáles cruzar.</summary>
    [HttpGet("sms/lista-negra/carteras")]
    public async Task<IActionResult> CarterasListaNegra(CancellationToken ct) =>
        Ok(await _sms.ListarCarterasListaNegraAsync(ct));

    /// <summary>Asesores del líder (Cartera_Agente) con su número/link persistidos.</summary>
    [HttpGet("sms/asesores")]
    public async Task<IActionResult> Asesores([FromQuery] int? liderID, CancellationToken ct)
    {
        if (ResolverLider(liderID, out var lider) is { } error) return error;
        try   { return Ok(await _sms.ListarAsesoresDelLiderAsync(lider, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
    }

    /// <summary>Todos los agentes activos, para agregar un asesor "prestado" de otro líder.</summary>
    [HttpGet("sms/agentes-activos")]
    public async Task<IActionResult> AgentesActivos(CancellationToken ct) =>
        Ok(await _sms.ListarAgentesActivosAsync(ct));

    /// <summary>Upsert de número corporativo + link por asesor. Se llama al pasar del paso 2 al 3.</summary>
    [HttpPut("sms/asesores/contactos")]
    public async Task<IActionResult> GuardarContactos([FromBody] List<ContactoAsesorRequest> contactos, CancellationToken ct)
    {
        var actualizados = await _sms.GuardarContactosAsync(contactos ?? [], Usuario, ct);
        return Ok(new { actualizados });
    }

    // ── Paso 1: validar ─────────────────────────────────────────────────────

    [HttpPost("sms/validar")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Validar(
        [FromForm] IFormFile archivo,
        [FromForm] int? liderID,
        [FromForm] List<string>? carterasListaNegra,
        [FromForm] IFormFile? archivoListaNegra,
        CancellationToken ct)
    {
        if (ValidarArchivo(archivo, "el archivo de titulares", ".xlsx", ".csv") is { } e1) return e1;
        if (archivoListaNegra is not null && ValidarArchivo(archivoListaNegra, "la lista negra", ".xlsx", ".csv") is { } e2) return e2;
        if (ResolverLider(liderID, out var lider) is { } e3) return e3;

        try
        {
            using var stream   = archivo.OpenReadStream();
            using var streamLn = archivoListaNegra?.OpenReadStream();
            return Ok(await _sms.ValidarAsync(Entrada(archivo, stream, lider, carterasListaNegra, archivoListaNegra, streamLn), ct));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { mensaje = $"No se pudo analizar el archivo: {ex.Message}" });
        }
    }

    // ── Paso 3: catálogo anotado ────────────────────────────────────────────

    /// <param name="links">JSON: array de strings con los links de los asesores del paso 2.</param>
    [HttpPost("sms/mensajes-disponibles")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> MensajesDisponibles(
        [FromForm] IFormFile archivo,
        [FromForm] string huella,
        [FromForm] string links,
        [FromForm] int? liderID,
        [FromForm] List<string>? carterasListaNegra,
        [FromForm] IFormFile? archivoListaNegra,
        CancellationToken ct)
    {
        if (ValidarArchivo(archivo, "el archivo de titulares", ".xlsx", ".csv") is { } e1) return e1;
        if (string.IsNullOrWhiteSpace(huella))
            return Conflict(new { mensaje = "Falta la huella del archivo validado. Vuelve al paso 1." });
        if (ResolverLider(liderID, out var lider) is { } e3) return e3;

        List<string> listaLinks;
        try { listaLinks = JsonSerializer.Deserialize<List<string>>(links ?? "[]", Json) ?? []; }
        catch (JsonException) { return BadRequest(new { mensaje = "El campo 'links' no es un JSON válido." }); }

        try
        {
            using var stream   = archivo.OpenReadStream();
            using var streamLn = archivoListaNegra?.OpenReadStream();
            var entrada = Entrada(archivo, stream, lider, carterasListaNegra, archivoListaNegra, streamLn);
            return Ok(await _sms.MensajesDisponiblesAsync(entrada, huella, listaLinks, ct));
        }
        catch (EstrategiaSmsConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex)            { return NotFound(new { mensaje = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { mensaje = $"No se pudo evaluar el catálogo: {ex.Message}" });
        }
    }

    // ── Paso 4: generar ─────────────────────────────────────────────────────

    /// <param name="asesores">JSON: array de ContactoAsesorRequest (agenteID, numeroCorporativo, linkWhatsapp).</param>
    [HttpPost("sms/generar")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Generar(
        [FromForm] IFormFile archivo,
        [FromForm] string huella,
        [FromForm] int mensajeAutorizadoID,
        [FromForm] string asesores,
        [FromForm] int? liderID,
        [FromForm] List<string>? carterasListaNegra,
        [FromForm] IFormFile? archivoListaNegra,
        CancellationToken ct)
    {
        if (ValidarArchivo(archivo, "el archivo de titulares", ".xlsx", ".csv") is { } e1) return e1;
        if (string.IsNullOrWhiteSpace(huella))
            return Conflict(new { mensaje = "Falta la huella del archivo validado. Vuelve al paso 1." });
        if (ResolverLider(liderID, out var lider) is { } e3) return e3;

        List<ContactoAsesorRequest> listaAsesores;
        try { listaAsesores = JsonSerializer.Deserialize<List<ContactoAsesorRequest>>(asesores ?? "[]", Json) ?? []; }
        catch (JsonException) { return BadRequest(new { mensaje = "El campo 'asesores' no es un JSON válido." }); }

        try
        {
            using var stream   = archivo.OpenReadStream();
            using var streamLn = archivoListaNegra?.OpenReadStream();
            var entrada = Entrada(archivo, stream, lider, carterasListaNegra, archivoListaNegra, streamLn);
            var request = new GenerarEstrategiaSmsRequest { MensajeAutorizadoID = mensajeAutorizadoID, Asesores = listaAsesores };

            var r = await _sms.GenerarAsync(entrada, huella, request, Usuario, ct);

            Response.Headers.Append("X-Estrategia-ID",     r.EstrategiaID.ToString());
            Response.Headers.Append("X-Total-Generados",   r.TotalGenerados.ToString());
            Response.Headers.Append("X-Excluidos-Longitud", r.ExcluidosLongitud.ToString());
            Response.Headers.Append("Access-Control-Expose-Headers", "X-Estrategia-ID, X-Total-Generados, X-Excluidos-Longitud, Content-Disposition");
            return File(r.Xlsx, Xlsx, r.NombreArchivo);
        }
        catch (EstrategiaSmsConflictoException ex) { return Conflict(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException ex)            { return NotFound(new { mensaje = ex.Message }); }
        catch (InvalidOperationException ex)       { return BadRequest(new { mensaje = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { mensaje = $"No se pudo generar el archivo: {ex.Message}" });
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.API.Controllers;

/// <summary>
/// Administración → Cargue de Fuentes de Datos (ADR-039). Validar y confirmar corren en
/// segundo plano: responden 202 con la foto del job y el front hace polling a
/// <c>GET jobs/{id}</c>. El usuario puede irse y retomar el job al volver.
/// </summary>
[ApiController]
[Route("api/cargue-fuentes")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class CargueFuentesController : ControllerBase
{
    // Un CSV de ~1M de filas de una fuente pesa del orden de 100 MB. web.config ya admite
    // 120 MB (IIS filtra antes que este atributo).
    private const long MaxBytes = 120L * 1024 * 1024;

    private readonly ICargueFuentesService _service;

    public CargueFuentesController(ICargueFuentesService service) => _service = service;

    private string Usuario => User.FindFirstValue("userName") ?? User.Identity?.Name ?? "desconocido";

    /// <summary>Fuentes cargables con sus columnas esperadas.</summary>
    [HttpGet("fuentes")]
    public IActionResult Fuentes() => Ok(_service.ListarFuentes());

    /// <summary>Sube el archivo y lanza la validación. 202 con el job; 409 si ya hay un cargue activo de la fuente.</summary>
    [HttpPost("{fuente}/validar")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Validar(string fuente, [FromForm] IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe adjuntar un archivo (.csv o .xlsx)." });

        try
        {
            await using var stream = archivo.OpenReadStream();
            var job = await _service.IniciarValidacionAsync(fuente, stream, archivo.FileName, archivo.Length, Usuario, ct);
            return AcceptedAtAction(nameof(Job), new { id = job.JobId }, job);
        }
        catch (CargueFuentesConflictoException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Cargues en memoria + ejecuciones registradas en las últimas horas.</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken ct) => Ok(await _service.ListarAsync(ct));

    /// <summary>Foto del job (polling). 404 si no existe o ya se purgó.</summary>
    [HttpGet("jobs/{id:guid}")]
    public IActionResult Job(Guid id) =>
        _service.Obtener(id) is { } job
            ? Ok(job)
            : NotFound(new { mensaje = "El cargue no existe o ya expiró (se conservan 48 h; un reinicio del servidor los borra)." });

    /// <summary>Confirma el preview y lanza la carga. 202; 409 si el job no está en PreviewListo o hay otro activo.</summary>
    [HttpPost("jobs/{id:guid}/confirmar")]
    public async Task<IActionResult> Confirmar(Guid id, CancellationToken ct)
    {
        try
        {
            var job = await _service.ConfirmarAsync(id, Usuario, ct);
            return job is null
                ? NotFound(new { mensaje = "El cargue no existe o ya expiró." })
                : AcceptedAtAction(nameof(Job), new { id }, job);
        }
        catch (CargueFuentesConflictoException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>Cancela la validación o la carga en curso (la carga se revierte entera).</summary>
    [HttpPost("jobs/{id:guid}/cancelar")]
    public IActionResult Cancelar(Guid id)
    {
        try
        {
            return _service.Cancelar(id, Usuario) is { } job
                ? Ok(job)
                : NotFound(new { mensaje = "El cargue no existe o ya expiró." });
        }
        catch (CargueFuentesConflictoException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>Descarta un job terminado o con preview (borra su archivo). 409 si sigue en curso.</summary>
    [HttpDelete("jobs/{id:guid}")]
    public IActionResult Descartar(Guid id)
    {
        try
        {
            return _service.Descartar(id)
                ? NoContent()
                : NotFound(new { mensaje = "El cargue no existe o ya expiró." });
        }
        catch (CargueFuentesConflictoException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
}

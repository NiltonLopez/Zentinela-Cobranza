using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Personas del call center (líderes y asesores). Por ahora solo la foto para los avatares de los
/// Informes de Localización (ADR-040).
/// </summary>
[ApiController]
[Route("api/personas")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
public class PersonasController : ControllerBase
{
    private readonly IFotoPersonaService _fotos;

    public PersonasController(IFotoPersonaService fotos) => _fotos = fotos;

    /// <summary>
    /// Miniatura cuadrada en JPEG de la foto de RRHH de un líder o asesor activo.
    /// 404 sin cuerpo si no tiene foto (o el documento no es de un líder/asesor activo: se
    /// responde igual para no revelar quién existe). 304 si el navegador ya la tiene
    /// (<c>If-None-Match</c> = ETag).
    /// </summary>
    /// <param name="documento">Cédula.</param>
    /// <param name="tam">Lado en px: 48, 96, 160 o 320 (otro valor → 160).</param>
    [HttpGet("{documento:long}/foto")]
    public async Task<IActionResult> Foto(long documento, [FromQuery] int tam = 160, CancellationToken ct = default)
    {
        var miniatura = await _fotos.ObtenerMiniaturaAsync(documento, tam, ct);
        if (miniatura is null) return NotFound();

        var (jpeg, etag) = miniatura.Value;

        // private: la foto es de una persona y la pide un usuario autenticado; no debe quedar en
        // cachés compartidos. Un día de vida en el navegador; el front además la retiene en
        // react-query (staleTime Infinity) mientras la sesión esté abierta.
        Response.Headers[HeaderNames.CacheControl] = "private, max-age=86400";
        Response.Headers[HeaderNames.ETag] = etag;

        var siNoCoincide = Request.Headers[HeaderNames.IfNoneMatch];
        if (siNoCoincide.Any(v => v is not null && v.Split(',').Any(e => e.Trim() == etag || e.Trim() == "*")))
            return StatusCode(StatusCodes.Status304NotModified);

        return File(jpeg, "image/jpeg");
    }
}

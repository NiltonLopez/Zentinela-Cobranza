using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/calificador")]
public class CalificadorCarteraController : ControllerBase
{
    private readonly ICalificadorCarteraService _service;

    public CalificadorCarteraController(ICalificadorCarteraService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lista de combinaciones Cartera+Líder disponibles según el rol del usuario.
    /// Auxiliar de Sistemas también accede al dropdown (lo necesita en Gestión de Asignaciones).
    /// </summary>
    [HttpGet("opciones")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.AuxiliarSistemas},{Roles.LiderExterno}")]
    public async Task<IActionResult> ObtenerOpciones()
    {
        var contexto = ObtenerContexto();
        var opciones = await _service.ObtenerOpcionesAsync(contexto);
        return Ok(opciones);
    }

    /// <summary>
    /// Calcula todas las métricas para la asignación Cartera+Líder indicada.
    /// Si <paramref name="liderID"/> se omite, se evalúa la cartera completa
    /// (todos los líderes asociados).
    /// </summary>
    [HttpGet("{carteraID:int}/{liderID:int?}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.LiderExterno}")]
    public async Task<IActionResult> Calificar(int carteraID, int? liderID, CancellationToken ct)
    {
        var resultado = await _service.CalificarAsync(carteraID, liderID, ct);
        return Ok(resultado);
    }

    /// <summary>
    /// Descarga CSV con (Cédula, Nombre, Última Consulta) de los titulares que
    /// clasifican en la celda indicada de la tabla "Porcentajes de Localización".
    /// </summary>
    /// <param name="fuente">Slug: cajacopi, camara, salud-total, fosyga, compensados, positiva, tigo, sos, claro, mutualser, sura, positiva-empresa, rues, simit, rgm.</param>
    /// <param name="categoria">encontrado | no-encontrado | falta.</param>
    /// <param name="liderID">Opcional. Omitir para "Cartera Completo".</param>
    [HttpGet("{carteraID:int}/localizacion/{fuente}/{categoria}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.LiderExterno}")]
    public async Task DescargarLocalizacionCsv(
        int carteraID,
        string fuente,
        string categoria,
        [FromQuery] int? liderID = null)
    {
        // Resolver el nombre de archivo antes de empezar a escribir el body
        // (Content-Disposition es header).
        var nombreArchivo = await _service.ResolverNombreArchivoLocalizacionAsync(
            carteraID, liderID, fuente, categoria);

        // El nombre puede traer tildes o Ñ (p. ej. "...CAMAÑO..."). Los valores de header
        // HTTP deben ser ASCII, así que se usa ContentDispositionHeaderValue: emite un
        // filename ASCII de respaldo y un filename* (RFC 5987, UTF-8) para el nombre real.
        Response.ContentType = "text/csv; charset=utf-8";
        var contentDisposition = new ContentDispositionHeaderValue("attachment");
        contentDisposition.SetHttpFileName(nombreArchivo);
        Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        await _service.EscribirLocalizacionCsvAsync(
            carteraID, liderID, fuente, categoria, Response.Body, HttpContext.RequestAborted);
    }

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);
}

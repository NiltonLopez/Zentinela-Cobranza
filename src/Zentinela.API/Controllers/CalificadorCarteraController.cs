using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        try
        {
            var resultado = await _service.CalificarAsync(carteraID, liderID, ct);
            return Ok(resultado);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // El cliente canceló (cambió de cartera/módulo o cerró la pestaña) — no hay a quién responder.
            // ADO.NET no siempre lanza OperationCanceledException al cancelar a mitad de un
            // ExecuteReaderAsync: a veces la envuelve en SqlException ("Operation cancelled by
            // user"), así que el filtro se basa en ct.IsCancellationRequested, no en el tipo.
            return new EmptyResult();
        }
        catch (SqlException ex) when (ex.Number == -2 || ex.Number == 11)
        {
            // -2 = timeout de comando, 11 = error de red a mitad de consulta.
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                "La consulta tardó demasiado. Intenta con una cartera más pequeña o vuelve a intentarlo.");
        }
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

    /// <summary>
    /// Descarga múltiple: ZIP con un CSV por celda seleccionada en "Porcentajes de
    /// Localización". Es POST y no GET porque la selección puede llegar a 60 celdas
    /// (20 fuentes × 3 categorías) y no cabe cómoda en un query string.
    /// </summary>
    [HttpPost("{carteraID:int}/localizacion/zip")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.LiderExterno}")]
    public async Task DescargarLocalizacionZip(
        int carteraID,
        [FromBody] DescargaLocalizacionZipRequest request,
        [FromQuery] int? liderID = null)
    {
        var celdas = (request?.Celdas ?? [])
            .Select(c => new CeldaLocalizacion(c.Fuente, c.Categoria))
            .ToList();

        if (celdas.Count == 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { mensaje = "Debe seleccionar al menos una celda." });
            return;
        }

        var nombreArchivo = await _service.ResolverNombreArchivoLocalizacionZipAsync(carteraID, liderID);

        // OBLIGATORIO para el ZIP, no es un ajuste opcional: ASP.NET Core prohíbe I/O
        // síncrono sobre Response.Body desde 3.0 (AllowSynchronousIO = false), y
        // ZipArchive NO implementa IAsyncDisposable — al cerrarse escribe el directorio
        // central con Stream.Write() síncrono. Sin esto, Dispose lanza
        // "Synchronous operations are disallowed" DESPUÉS de enviar los headers, y el
        // navegador recibe un archivo de 0 bytes con status 200 (reproducido 2026-08-19:
        // sin el flag 0 bytes + excepción; con el flag, ZIP íntegro).
        // El flag es por request, no global. Aceptable acá: es un endpoint de descarga
        // administrativa, puntual y de baja concurrencia.
        var controlCuerpo = HttpContext.Features.Get<IHttpBodyControlFeature>();
        if (controlCuerpo is not null) controlCuerpo.AllowSynchronousIO = true;

        Response.ContentType = "application/zip";
        var contentDisposition = new ContentDispositionHeaderValue("attachment");
        contentDisposition.SetHttpFileName(nombreArchivo);
        Response.Headers.Append("Content-Disposition", contentDisposition.ToString());

        await _service.EscribirLocalizacionZipAsync(
            carteraID, liderID, celdas, Response.Body, HttpContext.RequestAborted);
    }

    public record CeldaLocalizacionRequest(string Fuente, string Categoria);
    public record DescargaLocalizacionZipRequest(List<CeldaLocalizacionRequest> Celdas);

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);
}

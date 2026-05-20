using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.DTOs.CreadorZentinela;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/creador")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.LiderExterno}")]
public class CreadorZentinelaController : ControllerBase
{
    private readonly ICreadorZentinelaService _service;

    public CreadorZentinelaController(ICreadorZentinelaService service)
    {
        _service = service;
    }

    /// <summary>
    /// Retorna el catálogo de columnas disponibles en la Base Madre.
    /// Se usa para construir el selector de columnas en el frontend.
    /// </summary>
    [HttpGet("columnas")]
    public IActionResult ObtenerColumnas()
    {
        return Ok(_service.ObtenerColumnas());
    }

    /// <summary>
    /// Opciones del selector superior. Construido desde Lider_Cartera.
    /// Líder ve sus carteras (etiqueta solo NombreCartera).
    /// Admin/Director ve cada (Cartera × Líder) más una entrada agregada
    /// "Cartera Completo" por cartera (LiderID = null).
    /// </summary>
    [HttpGet("opciones")]
    public async Task<IActionResult> ObtenerOpciones()
    {
        var contexto = ObtenerContexto();
        var opciones = await _service.ObtenerOpcionesAsync(contexto);
        return Ok(opciones);
    }

    /// <summary>
    /// Retorna las primeras 100 filas que cumplen los filtros, más el total real.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] FiltroBaseMadreRequest filtro)
    {
        var contexto = ObtenerContexto();
        var resultado = await _service.ObtenerPreviewAsync(contexto, filtro);
        return Ok(resultado);
    }

    /// <summary>
    /// Genera y descarga el Excel completo con las columnas seleccionadas.
    /// </summary>
    [HttpPost("exportar")]
    public async Task<IActionResult> Exportar([FromBody] FiltroBaseMadreRequest filtro)
    {
        var contexto = ObtenerContexto();
        var (bytes, nombreArchivo) = await _service.ExportarExcelAsync(contexto, filtro);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombreArchivo);
    }

    /// <summary>
    /// Descarga streaming en CSV. No materializa la cartera en memoria —
    /// camino recomendado para carteras grandes (≥100k filas) donde el
    /// .xlsx sale demasiado pesado o el endpoint /exportar se acerca al
    /// timeout. Excel ES-CO abre el CSV directo (BOM UTF-8 + separador `;`).
    /// </summary>
    [HttpPost("exportar-csv")]
    public async Task ExportarCsv([FromBody] FiltroBaseMadreRequest filtro)
    {
        var contexto = ObtenerContexto();

        // Content-Disposition es header → resolver el nombre antes de empezar
        // a escribir el body. ResolverNombreArchivoAsync hace una sola query
        // ligera (lista de carteras, ya cacheada en EF Core).
        var nombreArchivo = await _service.ResolverNombreArchivoAsync(filtro.CarteraID, "csv");

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.Append(
            "Content-Disposition",
            $"attachment; filename=\"{nombreArchivo}\"");

        await _service.EscribirCsvAsync(
            contexto, filtro, Response.Body, HttpContext.RequestAborted);
    }

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);
}

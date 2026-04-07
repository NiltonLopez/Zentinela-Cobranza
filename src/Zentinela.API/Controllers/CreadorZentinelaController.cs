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
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
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

    private ContextoUsuario ObtenerContexto() => new(
        Rol:     User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
        LiderID: int.TryParse(User.FindFirstValue("liderID"), out var id) ? id : null);
}

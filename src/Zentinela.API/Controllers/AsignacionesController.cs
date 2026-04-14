using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Asignaciones;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/asignaciones")]
[Authorize(Roles = Roles.Admin)]
public class AsignacionesController : ControllerBase
{
    private readonly IAsignacionMasivaService _service;

    public AsignacionesController(IAsignacionMasivaService service)
    {
        _service = service;
    }

    /// <summary>Descarga la plantilla Excel lista para rellenar con cédulas.</summary>
    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla()
    {
        var bytes = _service.GenerarPlantilla();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla_asignaciones.xlsx");
    }

    /// <summary>
    /// Valida el Excel subido contra la BD y devuelve una vista previa
    /// con los primeros 10 registros y los conteos totales.
    /// </summary>
    [HttpPost("validar")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Validar(
        IFormFile archivo, [FromForm] int carteraID, [FromForm] int liderID)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo no puede estar vacío." });

        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El archivo debe ser .xlsx." });

        using var stream = archivo.OpenReadStream();
        var preview = await _service.ValidarExcelAsync(stream, carteraID, liderID);
        return Ok(preview);
    }

    /// <summary>
    /// Inserta los titularIDs validados en la staging y ejecuta el SP
    /// para transferirlos a Titular_Cartera.
    /// </summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarCargueRequest request)
    {
        if (request.TitularIDs.Count == 0)
            return BadRequest(new { mensaje = "No hay titulares para confirmar." });

        var resultado = await _service.ConfirmarCargueAsync(
            request.CarteraID, request.LiderID, request.TitularIDs);
        return Ok(resultado);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Asignaciones;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/asignaciones")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class AsignacionesController : ControllerBase
{
    private readonly IAsignacionMasivaService _service;

    public AsignacionesController(IAsignacionMasivaService service)
    {
        _service = service;
    }

    /// <summary>
    /// Descarga la plantilla Excel lista para rellenar. Con completo=true incluye
    /// la columna LiderID por fila (modo "Cartera Completo").
    /// </summary>
    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla([FromQuery] bool completo = false)
    {
        var bytes  = completo ? _service.GenerarPlantillaCompleto() : _service.GenerarPlantilla();
        var nombre = completo ? "plantilla_asignaciones_completo.xlsx" : "plantilla_asignaciones.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombre);
    }

    /// <summary>
    /// Valida el Excel subido contra la BD y devuelve una vista previa
    /// con los primeros 10 registros y los conteos totales. Si liderID es null,
    /// se valida en modo "Cartera Completo" (el LiderID viene por fila en el Excel).
    /// </summary>
    [HttpPost("validar")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Validar(
        IFormFile archivo, [FromForm] int carteraID, [FromForm] int? liderID)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo no puede estar vacío." });

        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El archivo debe ser .xlsx." });

        using var stream = archivo.OpenReadStream();
        var preview = liderID.HasValue
            ? await _service.ValidarExcelAsync(stream, carteraID, liderID.Value)
            : await _service.ValidarExcelCompletoAsync(stream, carteraID);
        return Ok(preview);
    }

    /// <summary>
    /// Confirma el cargue reemplazando en Titular_Cartera. Con LiderID → reemplaza
    /// solo ese líder; sin LiderID (modo "Cartera Completo") → reemplaza toda la
    /// cartera con los pares (TitularID, LiderID) del archivo.
    /// </summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarCargueRequest request)
    {
        if (request.LiderID.HasValue)
        {
            if (request.TitularIDs.Count == 0)
                return BadRequest(new { mensaje = "No hay titulares para confirmar." });

            var resultado = await _service.ConfirmarCargueAsync(
                request.CarteraID, request.LiderID.Value, request.TitularIDs, request.NuevosTitulares);
            return Ok(resultado);
        }

        // Modo "Cartera Completo"
        if (request.Asignaciones.Count == 0)
            return BadRequest(new { mensaje = "No hay asignaciones para confirmar." });

        var resultadoCompleto = await _service.ConfirmarCargueCompletoAsync(
            request.CarteraID, request.Asignaciones, request.NuevosTitulares);
        return Ok(resultadoCompleto);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Reportes;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/reportes")]
[Authorize(Roles = Roles.Admin)]
public class CargueReportesController : ControllerBase
{
    private readonly ICargueReporteService _service;

    public CargueReportesController(ICargueReporteService service)
    {
        _service = service;
    }

    /// <summary>Descarga la plantilla Excel con los 14 campos del reporte.</summary>
    [HttpGet("plantilla")]
    public IActionResult DescargarPlantilla()
    {
        var bytes = _service.GenerarPlantilla();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla_reporte_estados.xlsx");
    }

    /// <summary>
    /// Valida el Excel subido: verifica TitularID y AgenteID contra BD.
    /// CarteraID viene del selector de la UI (no del archivo).
    /// Devuelve preview con primeros 10 registros, conteos y las filas válidas completas.
    /// </summary>
    [HttpPost("validar")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB — reportes pueden ser grandes
    public async Task<IActionResult> Validar([FromForm] IFormFile archivo, [FromForm] int carteraID)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo no puede estar vacío." });

        if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El archivo debe ser .xlsx." });

        if (carteraID <= 0)
            return BadRequest(new { mensaje = "Debe seleccionar una cartera." });

        using var stream = archivo.OpenReadStream();
        var preview = await _service.ValidarExcelAsync(stream, carteraID);
        return Ok(preview);
    }

    /// <summary>
    /// Inserta las filas válidas en staging y ejecuta ambos SPs:
    /// UltimoEstadoTitular (UPSERT) e HistoricoEstadoTitular (INSERT append).
    /// </summary>
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarReporteRequest request)
    {
        if (request.FilasValidas.Count == 0)
            return BadRequest(new { mensaje = "No hay filas para confirmar." });

        var resultado = await _service.ConfirmarCargueAsync(request.FilasValidas);
        return Ok(resultado);
    }
}

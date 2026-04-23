using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Metas;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/metas")]
[Authorize(Roles = Roles.Admin)]
public class AsignacionMetasController : ControllerBase
{
    private readonly IAsignacionMetasService _service;

    public AsignacionMetasController(IAsignacionMetasService service)
    {
        _service = service;
    }

    /// <summary>
    /// Devuelve el estado de metas del mes actual: todas las combinaciones
    /// líder-cartera y agentes activos con sus metas y datos informativos.
    /// </summary>
    [HttpGet("estado-mes-actual")]
    public async Task<IActionResult> EstadoMesActual()
    {
        var resultado = await _service.ObtenerEstadoMesActualAsync();
        return Ok(resultado);
    }

    /// <summary>UPSERT de la meta para una combinación líder-cartera en el mes actual.</summary>
    [HttpPost("guardar-lider")]
    public async Task<IActionResult> GuardarLider([FromBody] GuardarMetaLiderRequest request)
    {
        if (request.Valor <= 0)
            return BadRequest(new { mensaje = "El valor de la meta debe ser mayor a cero." });

        var resultado = await _service.GuardarMetaLiderAsync(request);
        return Ok(resultado);
    }

    /// <summary>UPSERT de la meta para un agente en el mes actual.</summary>
    [HttpPost("guardar-agente")]
    public async Task<IActionResult> GuardarAgente([FromBody] GuardarMetaAgenteRequest request)
    {
        if (request.Valor <= 0)
            return BadRequest(new { mensaje = "El valor de la meta debe ser mayor a cero." });

        var resultado = await _service.GuardarMetaAgenteAsync(request);
        return Ok(resultado);
    }

}

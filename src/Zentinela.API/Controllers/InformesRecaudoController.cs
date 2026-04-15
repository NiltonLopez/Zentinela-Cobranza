using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/informes/recaudo")]
[Authorize(Roles = "Admin,Líder,Director")]
public class InformesRecaudoController : ControllerBase
{
    private readonly IInformesRecaudoService _service;

    public InformesRecaudoController(IInformesRecaudoService service) => _service = service;

    /// <summary>
    /// Sub-informe de agentes: recaudo, honorarios, meta y cumplimiento.
    /// Ordenado de menor a mayor recaudo.
    /// </summary>
    [HttpGet("agentes")]
    public async Task<IActionResult> GetAgentes([FromQuery] int año, [FromQuery] int mes)
    {
        if (mes < 1 || mes > 12) return BadRequest(new { mensaje = "Mes inválido." });
        var resultado = await _service.GetInformeAgentesAsync(año, mes);
        return Ok(resultado);
    }

    /// <summary>
    /// Sub-informe de líderes: carteras, capacity, meta y proyección de cierre.
    /// Una fila por combinación líder-cartera.
    /// </summary>
    [HttpGet("lideres")]
    public async Task<IActionResult> GetLideres([FromQuery] int año, [FromQuery] int mes)
    {
        if (mes < 1 || mes > 12) return BadRequest(new { mensaje = "Mes inválido." });
        var resultado = await _service.GetInformeLideresAsync(año, mes);
        return Ok(resultado);
    }

    /// <summary>
    /// Sub-informes de carteras y comparativo: recaudo por cartera entre dos períodos.
    /// Para "mes actual vs anterior" pasa los dos períodos deseados.
    /// </summary>
    [HttpGet("carteras")]
    public async Task<IActionResult> GetCarteras(
        [FromQuery] int añoA, [FromQuery] int mesA,
        [FromQuery] int añoB, [FromQuery] int mesB)
    {
        if (mesA < 1 || mesA > 12 || mesB < 1 || mesB > 12)
            return BadRequest(new { mensaje = "Mes inválido." });

        var resultado = await _service.GetInformeCarterasAsync(añoA, mesA, añoB, mesB);
        return Ok(resultado);
    }
}

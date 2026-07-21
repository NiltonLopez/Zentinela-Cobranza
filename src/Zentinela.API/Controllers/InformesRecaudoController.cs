using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/informes/recaudo")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
public class InformesRecaudoController : ControllerBase
{
    private readonly IInformesRecaudoService _service;
    private readonly ICategoriaRankingService _categoriaRanking;

    public InformesRecaudoController(IInformesRecaudoService service, ICategoriaRankingService categoriaRanking)
    {
        _service = service;
        _categoriaRanking = categoriaRanking;
    }

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
    /// Desglose de una cartera de "BASE GENERAL": líderes participantes según el asesor que
    /// registró cada pago, más una fila agregada para los pagos sin asesor.
    /// </summary>
    [HttpGet("cartera-base-general")]
    public async Task<IActionResult> GetCarteraBaseGeneral(
        [FromQuery] int carteraID, [FromQuery] int año, [FromQuery] int mes)
    {
        if (mes < 1 || mes > 12) return BadRequest(new { mensaje = "Mes inválido." });
        var resultado = await _service.GetInformeCarteraBaseGeneralAsync(carteraID, año, mes);
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

    /// <summary>
    /// Sub-informe de agencias externas: recaudo, honorarios y asignaciones por
    /// (líder, cartera externa) para el mes indicado. Complemento del informe de
    /// líderes — solo incluye carteras con EsExterna = 1.
    /// </summary>
    [HttpGet("agencias-externas")]
    public async Task<IActionResult> GetAgenciasExternas([FromQuery] int año, [FromQuery] int mes)
    {
        if (mes < 1 || mes > 12) return BadRequest(new { mensaje = "Mes inválido." });
        var resultado = await _service.GetInformeAgenciasExternasAsync(año, mes);
        return Ok(resultado);
    }

    /// <summary>
    /// Categoría de ranking (Diamante/Oro/Plata/Bronce) de todos los asesores internos
    /// para el año/mes indicado, con el desglose del cálculo (promedio anual, honorario
    /// del mes actual/anterior, si se aplicó el ajuste por bajo rendimiento) — para que
    /// admin/director puedan auditar por qué un asesor quedó en tal categoría.
    /// Solo Admin/Director: es información de toda la empresa, no de un líder puntual.
    /// </summary>
    [HttpGet("categorias")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Director}")]
    public async Task<IActionResult> GetCategorias([FromQuery] int año, [FromQuery] int mes)
    {
        if (mes < 1 || mes > 12) return BadRequest(new { mensaje = "Mes inválido." });
        var resultado = await _categoriaRanking.ObtenerCategoriasAsync(año, mes);
        return Ok(resultado.Values.OrderByDescending(c => c.PromedioAnual));
    }
}

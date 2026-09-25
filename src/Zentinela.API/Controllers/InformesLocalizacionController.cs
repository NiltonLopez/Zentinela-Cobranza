using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.API.Extensions;
using Zentinela.Application;
using Zentinela.Application.DTOs.InformesLocalizacion;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Utils;

namespace Zentinela.API.Controllers;

/// <summary>
/// Informes de Localización (ADR-040): resumen, cobertura por fuente, frescura y rankings con foto,
/// leídos del último corte del precálculo.
///
/// Alcance: Admin y Director ven todo. Un Líder ve solo lo suyo: <c>TryResolverLiderID</c> ignora el
/// <c>liderID</c> de la URL y usa el del token, y si pide una <c>carteraID</c> tiene que estar en su
/// Lider_Cartera activo. Como su liderID siempre queda fijado, un Líder nunca obtiene la cartera completa.
///
/// Errores: 503 mientras el usuario no aplique el script de las tablas; 409 si aún no hay corte.
/// </summary>
[ApiController]
[Route("api/informes/localizacion")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director}")]
public class InformesLocalizacionController : ControllerBase
{
    private readonly IInformesLocalizacionService _service;
    private readonly ILocalizacionResumenCoordinador _coordinador;

    public InformesLocalizacionController(IInformesLocalizacionService service, ILocalizacionResumenCoordinador coordinador)
    {
        _service = service;
        _coordinador = coordinador;
    }

    /// <summary>KPIs, índice, gráfico por fuente y alertas del alcance.</summary>
    [HttpGet("resumen")]
    public Task<IActionResult> Resumen([FromQuery] int? liderID, [FromQuery] int? carteraID, CancellationToken ct) =>
        ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerResumenAsync(l, c, ct)));

    /// <summary>Cobertura por fuente agrupada por par, cartera o líder.</summary>
    [HttpGet("cobertura")]
    public Task<IActionResult> Cobertura(
        [FromQuery] int? liderID, [FromQuery] int? carteraID, [FromQuery] string? agrupar, CancellationToken ct) =>
        ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerCoberturaAsync(l, c, agrupar ?? "par", ct)));

    /// <summary>Antigüedad de la última consulta por fuente, en tramos, con umbral de "dato viejo" (90/180/365).</summary>
    [HttpGet("frescura")]
    public Task<IActionResult> Frescura(
        [FromQuery] int? liderID, [FromQuery] int? carteraID, [FromQuery] int umbral = 180, CancellationToken ct = default)
    {
        if (umbral is not (90 or 180 or 365))
            return Task.FromResult<IActionResult>(BadRequest(new { mensaje = "El umbral debe ser 90, 180 o 365 días." }));
        return ConAlcance(liderID, carteraID, ct, async (l, c) => Ok(await _service.ObtenerFrescuraAsync(l, c, umbral, ct)));
    }

    /// <summary>
    /// Rankings de líderes (con foto) y de carteras. El Líder recibe el top más su propia fila, nunca la
    /// lista completa, y en carteras solo sus pares. <paramref name="incluirExternas"/> solo aplica a
    /// Admin/Director.
    /// </summary>
    [HttpGet("rankings")]
    public async Task<IActionResult> Rankings(
        [FromQuery] string? criterio, [FromQuery] bool incluirExternas = false, CancellationToken ct = default)
    {
        if (!CalculoLocalizacion.TryParseCriterio(criterio, out var c))
            return BadRequest(new { mensaje = "Criterio inválido: Indice, Contacto, Cobertura, Frescura o SinContactoSaldo." });

        // Sin liderID pedido: para el Líder devuelve el suyo (el del token); para el resto, null.
        if (!User.TryResolverLiderID(null, out var propio)) return Forbid();
        var esLider = EsLider;

        return await Traducir(async () =>
            Ok(await _service.ObtenerRankingsAsync(c, esLider ? false : incluirExternas, esLider ? propio : null, ct)));
    }

    /// <summary>
    /// Estado del precálculo: corte que se muestra ("Localización al …", "Gestión al …") y si hay uno
    /// calculándose. Abierto a los tres roles porque solo expone metadatos del corte: el front lo usa para
    /// el banner y para los estados "calculando" / "sin tablas".
    /// </summary>
    [HttpGet("resumen/estado")]
    public Task<IActionResult> Estado(CancellationToken ct) =>
        Traducir(async () => Ok(await _service.ObtenerEstadoAsync(ct)));

    /// <summary>Pide un corte nuevo en segundo plano (Admin). Responde 202 de inmediato.</summary>
    [HttpPost("resumen/recalcular")]
    [Authorize(Roles = Roles.Admin)]
    public Task<IActionResult> Recalcular(CancellationToken ct) =>
        Traducir(async () =>
        {
            // Si faltan las tablas, que el Admin lo vea ya (503) y no como un corte fallido en el log.
            await _service.ObtenerEstadoAsync(ct);
            _coordinador.SolicitarRecalculo("MANUAL", User.FindFirstValue("userName") ?? User.Identity?.Name);
            return Accepted(new { mensaje = "Recálculo solicitado. El corte nuevo aparece en cuanto termine (≈1 minuto)." });
        });

    // ── Alcance y errores ─────────────────────────────────────────────────────

    // Mismo criterio que AlcanceLiderExtensions (claim de rol del token).
    private bool EsLider => User.FindFirstValue(ClaimTypes.Role) == Roles.Lider;

    private async Task<IActionResult> ConAlcance(
        int? liderID, int? carteraID, CancellationToken ct, Func<int?, int?, Task<IActionResult>> accion)
    {
        if (!User.TryResolverLiderID(liderID, out var efectivo)) return Forbid();

        // La cartera de un Líder tiene que ser suya (y activa); con su liderID fijado queda en el par.
        if (EsLider && carteraID is { } c
            && !await _service.EsCarteraDelLiderAsync(efectivo!.Value, c, ct))
            return Forbid();

        return await Traducir(() => accion(efectivo, carteraID));
    }

    private async Task<IActionResult> Traducir(Func<Task<IActionResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (LocalizacionTablasFaltantesException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = ex.Message, script = LocalizacionTablasFaltantesException.Script, sinTablas = true });
        }
        catch (LocalizacionSinCorteException ex)
        {
            return Conflict(new { mensaje = ex.Message, calculando = ex.Calculando, sinCorte = true });
        }
    }
}

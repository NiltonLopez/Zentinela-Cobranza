using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.MensajesAutorizados;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

/// <summary>
/// Administración → Mensajes Autorizados (ADR-037). Catálogo cerrado de textos SMS que
/// escriben abogados o encargados; es lo único que el Creador de Virtualidad deja elegir.
/// Sin borrado físico: un mensaje usado queda referenciado por EstrategiaSms.
/// </summary>
[ApiController]
[Route("api/mensajes-autorizados")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Director}")]
public class MensajesAutorizadosController : ControllerBase
{
    private readonly IMensajesAutorizadosService _service;

    public MensajesAutorizadosController(IMensajesAutorizadosService service) => _service = service;

    private string Usuario => User.FindFirstValue("userName") ?? User.Identity?.Name ?? "desconocido";

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool soloActivos = false, CancellationToken ct = default) =>
        Ok(await _service.ListarAsync(soloActivos, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var m = await _service.ObtenerAsync(id, ct);
        return m is null ? NotFound(new { mensaje = "El mensaje no existe." }) : Ok(m);
    }

    /// <summary>Valida sin guardar: el formulario lo usa para marcar caracteres rechazados en vivo.</summary>
    [HttpPost("validar")]
    public IActionResult Validar([FromBody] GuardarMensajeAutorizadoRequest request)
    {
        try
        {
            _service.Validar(request);
            return Ok(new { valido = true });
        }
        catch (MensajeAutorizadoInvalidoException ex)
        {
            return Ok(new { valido = false, mensaje = ex.Message, caracteresRechazados = ex.CaracteresRechazados });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] GuardarMensajeAutorizadoRequest request, CancellationToken ct)
    {
        try
        {
            var creado = await _service.CrearAsync(request, Usuario, ct);
            return CreatedAtAction(nameof(Obtener), new { id = creado.ID }, creado);
        }
        catch (MensajeAutorizadoInvalidoException ex)
        {
            return BadRequest(new { mensaje = ex.Message, caracteresRechazados = ex.CaracteresRechazados });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] GuardarMensajeAutorizadoRequest request, CancellationToken ct)
    {
        try
        {
            var actualizado = await _service.ActualizarAsync(id, request, Usuario, ct);
            return actualizado is null ? NotFound(new { mensaje = "El mensaje no existe." }) : Ok(actualizado);
        }
        catch (MensajeAutorizadoInvalidoException ex)
        {
            return BadRequest(new { mensaje = ex.Message, caracteresRechazados = ex.CaracteresRechazados });
        }
    }

    [HttpPatch("{id:int}/activo")]
    public async Task<IActionResult> CambiarActivo(int id, [FromBody] CambiarActivoRequest request, CancellationToken ct)
    {
        var ok = await _service.CambiarActivoAsync(id, request.Activo, Usuario, ct);
        return ok ? Ok(new { id, activo = request.Activo }) : NotFound(new { mensaje = "El mensaje no existe." });
    }
}

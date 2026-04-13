using ConsultaIntegrada.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/consulta")]
[Authorize]
public class ConsultaController : ControllerBase
{
    private readonly IConsultaService _service;

    public ConsultaController(IConsultaService service) => _service = service;

    [HttpGet("{cedula}")]
    public async Task<IActionResult> Get(string cedula)
    {
        try
        {
            var resultado = await _service.ConsultarPorCedulaAsync(cedula);

            if (!resultado.Encontrado)
                return NotFound(new { mensaje = $"La cédula {cedula} no existe en ninguna base de datos." });

            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = "Error interno al consultar.", detalle = ex.Message });
        }
    }
}

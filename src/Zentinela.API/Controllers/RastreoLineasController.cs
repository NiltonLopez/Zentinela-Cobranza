using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/consultas/rastreo")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Lider},{Roles.Director},{Roles.Asesor},{Roles.LiderExterno}")]
public class RastreoLineasController : ControllerBase
{
    private readonly IRastreoLineasService _service;

    public RastreoLineasController(IRastreoLineasService service)
        => _service = service;

    /// <summary>
    /// Busca un número celular (10 dígitos, debe iniciar en 3) en todas las
    /// tablas de contactabilidad y devuelve apariciones por tabla + titulares.
    /// </summary>
    [HttpGet("{numero}")]
    public async Task<IActionResult> Get(string numero)
    {
        if (string.IsNullOrWhiteSpace(numero) || numero.Length != 10
            || !numero.All(char.IsDigit) || numero[0] != '3')
        {
            return BadRequest(new { mensaje = "El número debe tener 10 dígitos y comenzar en 3." });
        }

        var resultado = await _service.RastrearAsync(numero);
        return Ok(resultado);
    }
}

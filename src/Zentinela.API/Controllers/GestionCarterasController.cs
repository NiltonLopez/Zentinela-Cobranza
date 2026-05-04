using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Carteras;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/gestion-carteras")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class GestionCarterasController : ControllerBase
{
    private readonly IGestionCarterasService _service;

    public GestionCarterasController(IGestionCarterasService service)
    {
        _service = service;
    }

    /// <summary>Lista todas las relaciones Líder_Cartera con conteo de asignaciones.</summary>
    [HttpGet("relaciones")]
    public async Task<IActionResult> ListarRelaciones()
    {
        var relaciones = await _service.ListarRelacionesAsync();
        return Ok(relaciones);
    }

    /// <summary>Lista todos los líderes activos para el dropdown.</summary>
    [HttpGet("lideres")]
    public async Task<IActionResult> ListarLideres()
    {
        var lideres = await _service.ListarLideresAsync();
        return Ok(lideres);
    }

    /// <summary>Busca carteras por nombre para el autocompletado (máx. 10 resultados).</summary>
    [HttpGet("carteras/buscar")]
    public async Task<IActionResult> BuscarCarteras([FromQuery] string q = "")
    {
        var carteras = await _service.BuscarCarterasAsync(q);
        return Ok(carteras);
    }

    /// <summary>Crea una relación Líder_Cartera. Crea la cartera si NombreCartera es nuevo.</summary>
    [HttpPost("relaciones")]
    public async Task<IActionResult> CrearRelacion([FromBody] CrearRelacionDto dto)
    {
        try
        {
            var resultado = await _service.CrearRelacionAsync(dto);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Elimina una relación Líder_Cartera. No elimina los Titular_Cartera asociados.</summary>
    [HttpDelete("relaciones/{liderID:int}/{carteraID:int}")]
    public async Task<IActionResult> EliminarRelacion(int liderID, int carteraID)
    {
        try
        {
            await _service.EliminarRelacionAsync(liderID, carteraID);
            return Ok(new { mensaje = "Relación eliminada correctamente." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

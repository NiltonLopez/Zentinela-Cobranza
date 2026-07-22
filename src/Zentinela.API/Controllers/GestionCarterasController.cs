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

    /// <summary>Lista empleados de RRHH elegibles para darse de alta como líder.</summary>
    [HttpGet("empleados-elegibles")]
    public async Task<IActionResult> ListarEmpleadosElegibles()
    {
        var empleados = await _service.ListarEmpleadosElegiblesAsync();
        return Ok(empleados);
    }

    /// <summary>Crea un líder en DBCallCenter a partir de un empleado de RRHH (por cédula).</summary>
    [HttpPost("lideres")]
    public async Task<IActionResult> CrearLider([FromBody] CrearLiderDto dto)
    {
        try
        {
            var lider = await _service.CrearLiderAsync(dto);
            return Ok(lider);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Crea un líder EXTERNO (no empleado) y su usuario Zentinela con rol Líder Externo.
    /// Devuelve las credenciales (usuario + contraseña temporal si se generó).
    /// </summary>
    [HttpPost("lideres-externos")]
    public async Task<IActionResult> CrearLiderExterno([FromBody] CrearLiderExternoDto dto)
    {
        try
        {
            var resultado = await _service.CrearLiderExternoAsync(dto);
            return Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
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

    /// <summary>Descarga un Excel con las asignaciones actuales (Cédula + Nombre) de la cartera.</summary>
    [HttpGet("relaciones/{liderID:int}/{carteraID:int}/asignacion")]
    public async Task<IActionResult> DescargarAsignacion(int liderID, int carteraID)
    {
        var bytes = await _service.DescargarAsignacionAsync(liderID, carteraID);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"asignacion_{carteraID}_{liderID}.xlsx");
    }

    /// <summary>
    /// Elimina FÍSICAMENTE una relación Líder_Cartera (solo si no tiene histórico).
    /// No elimina los Titular_Cartera asociados de forma manual — los limpia el servicio.
    /// </summary>
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

    /// <summary>
    /// Inactiva (soft-delete) una relación con histórico: vacía sus titulares y la retira
    /// de lo operativo conservando pagos/metas/mensajería/IVR. Reversible. Ver ADR-022.
    /// </summary>
    [HttpPut("relaciones/{liderID:int}/{carteraID:int}/inactivar")]
    public async Task<IActionResult> InactivarRelacion(int liderID, int carteraID)
    {
        try
        {
            await _service.InactivarRelacionAsync(liderID, carteraID);
            return Ok(new { mensaje = "Relación inactivada. Su histórico se conserva." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Reactiva una relación previamente inactivada. Ver ADR-022.</summary>
    [HttpPut("relaciones/{liderID:int}/{carteraID:int}/reactivar")]
    public async Task<IActionResult> ReactivarRelacion(int liderID, int carteraID)
    {
        try
        {
            await _service.ReactivarRelacionAsync(liderID, carteraID);
            return Ok(new { mensaje = "Relación reactivada correctamente." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

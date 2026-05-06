using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Admin;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.AuxiliarSistemas}")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// Lista todos los usuarios registrados en Zentinela con su rol y estado de contraseña.
    /// </summary>
    [HttpGet("usuarios")]
    public async Task<IActionResult> ListarUsuarios()
    {
        var usuarios = await _adminService.ListarUsuariosAsync();
        return Ok(usuarios);
    }

    /// <summary>
    /// Resetea la contraseña de un usuario al número de documento
    /// y activa el cambio obligatorio en el próximo login.
    /// </summary>
    [HttpPost("usuarios/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        try
        {
            await _adminService.ResetPasswordAsync(id);
            return Ok(new { mensaje = "Contraseña restablecida correctamente. El usuario deberá cambiarla en su próximo ingreso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Retorna el supervisor actual del usuario según Empleado.IDSupervisor en DBRecursosHumanos.
    /// </summary>
    [HttpGet("usuarios/{id:int}/supervisor")]
    public async Task<IActionResult> ObtenerSupervisorActual(int id)
    {
        var supervisor = await _adminService.ObtenerSupervisorActualAsync(id);
        return Ok(supervisor); // null = sin supervisor asignado
    }

    /// <summary>
    /// Retorna empleados con cargos supervisores (CargoID ∈ {2,7,8,13,18}).
    /// </summary>
    [HttpGet("supervisores")]
    public async Task<IActionResult> ListarSupervisores()
    {
        var supervisores = await _adminService.ListarSupervisoresAsync();
        return Ok(supervisores);
    }

    /// <summary>
    /// Retorna todos los empleados cuyo ID aparece como IDSupervisor de algún empleado.
    /// Fuente de datos para el filtro de supervisor en Gestión de Personal.
    /// </summary>
    [HttpGet("supervisores-activos")]
    public async Task<IActionResult> ListarSupervisoresActivos()
    {
        var supervisores = await _adminService.ListarSupervisoresActivosAsync();
        return Ok(supervisores);
    }

    /// <summary>
    /// Reasigna el supervisor de un usuario y limpia sus asignaciones de cartera anteriores.
    /// </summary>
    [HttpPost("usuarios/{id:int}/reasignar-supervisor")]
    public async Task<IActionResult> ReasignarSupervisor(int id, [FromBody] ReasignarSupervisorDto dto)
    {
        try
        {
            await _adminService.ReasignarSupervisorAsync(id, dto.NuevoSupervisorId);
            return Ok(new { mensaje = "Supervisor reasignado correctamente. Las asignaciones de cartera anteriores fueron eliminadas." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Inactiva un usuario: pone EstadoEmpleado = false en DBRecursosHumanos
    /// y elimina el ApplicationUser de Zentinela.
    /// </summary>
    [HttpDelete("usuarios/{id:int}")]
    public async Task<IActionResult> InactivarUsuario(int id)
    {
        try
        {
            await _adminService.InactivarUsuarioAsync(id);
            return Ok(new { mensaje = "Usuario inactivado y eliminado de Zentinela correctamente." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Cambia el UserName del usuario (ApplicationUser.UserName).
    /// </summary>
    [HttpPatch("usuarios/{id:int}/usuario")]
    public async Task<IActionResult> CambiarUsuario(int id, [FromBody] CambiarUsuarioDto dto)
    {
        try
        {
            await _adminService.CambiarUsuarioAsync(id, dto.NuevoUsuario);
            return Ok(new { mensaje = "Usuario actualizado correctamente." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista todos los agentes activos con su supervisor, líder resuelto y
    /// conteo de carteras asignadas en Cartera_Agente.
    /// </summary>
    [HttpGet("agentes")]
    public async Task<IActionResult> ListarAgentes()
    {
        var agentes = await _adminService.ListarAgentesAsync();
        return Ok(agentes);
    }

    /// <summary>
    /// Devuelve las carteras del líder del agente con flag de asignación.
    /// </summary>
    [HttpGet("agentes/{agenteID:long}/carteras")]
    public async Task<IActionResult> ObtenerCarterasDelAgente(long agenteID)
    {
        try
        {
            var carteras = await _adminService.ObtenerCarterasDelAgenteAsync(agenteID);
            return Ok(carteras);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Asigna una cartera al agente. El LiderID se resuelve server-side
    /// desde el supervisor del agente. Idempotente.
    /// </summary>
    [HttpPost("agentes/{agenteID:long}/carteras/{carteraID:int}")]
    public async Task<IActionResult> AsignarCarteraAgente(long agenteID, int carteraID)
    {
        try
        {
            await _adminService.AsignarCarteraAgenteAsync(agenteID, carteraID);
            return Ok(new { asignada = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Quita una cartera del agente. Idempotente.
    /// </summary>
    [HttpDelete("agentes/{agenteID:long}/carteras/{carteraID:int}")]
    public async Task<IActionResult> DesasignarCarteraAgente(long agenteID, int carteraID)
    {
        try
        {
            await _adminService.DesasignarCarteraAgenteAsync(agenteID, carteraID);
            return Ok(new { asignada = false });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

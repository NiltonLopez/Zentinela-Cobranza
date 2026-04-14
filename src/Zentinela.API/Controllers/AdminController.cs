using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
using Zentinela.Application.DTOs.Admin;
using Zentinela.Application.Interfaces;

namespace Zentinela.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
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
    /// Retorna empleados con cargos supervisores (CargoID ∈ {2,7,8,13,18}).
    /// </summary>
    [HttpGet("supervisores")]
    public async Task<IActionResult> ListarSupervisores()
    {
        var supervisores = await _adminService.ListarSupervisoresAsync();
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
}

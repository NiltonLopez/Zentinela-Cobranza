using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentinela.Application;
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
}

using Microsoft.AspNetCore.Identity;

namespace Zentinela.Infrastructure.SQLServer.Models;

public class ApplicationUser : IdentityUser<int>
{
    public string NombreCompleto { get; set; } = string.Empty;

    /// <summary>
    /// Cédula del empleado — FK lógica hacia DBRecursosHumanos.Empleado.NumeroDocumento
    /// y hacia DBCallCenter.Agente.AgenteID.
    /// </summary>
    public long NumeroDocumento { get; set; }

    /// <summary>
    /// FK lógica hacia DBCallCenter.Lider — integridad validada en lógica de negocio.
    /// Null para roles distintos de Líder.
    /// </summary>
    public int? LiderID { get; set; }

    /// <summary>
    /// True cuando el usuario se registra por primera vez.
    /// El sistema obliga cambio de contraseña antes de acceder a la aplicación.
    /// </summary>
    public bool RequiereCambioPassword { get; set; } = true;
}

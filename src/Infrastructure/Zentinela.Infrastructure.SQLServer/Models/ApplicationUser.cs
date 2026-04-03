using Microsoft.AspNetCore.Identity;

namespace Zentinela.Infrastructure.SQLServer.Models;

public class ApplicationUser : IdentityUser<int>
{
    // FK lógica hacia DBCallCenter.Lider — integridad validada en AuthService
    public int? LiderID { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
}

using Microsoft.AspNetCore.Identity;

namespace Zentinela.API.Domain.Entities;

public class ApplicationUser : IdentityUser<int>
{
    // Vincula al usuario con su entidad de negocio Líder (nullable: Admins no tienen Líder)
    public int? LiderID { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
}

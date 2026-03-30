using Microsoft.AspNetCore.Identity;
using Zentinela.API.Domain.Entities;

namespace Zentinela.API.Infrastructure.Data;

/// <summary>
/// Crea roles y usuario administrador inicial si no existen.
/// Solo se ejecuta en entorno de desarrollo.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Crear roles del sistema
        string[] roles = ["Admin", "Supervisor", "Líder", "Analista", "Asesor"];
        foreach (var rol in roles)
        {
            if (!await roleManager.RoleExistsAsync(rol))
                await roleManager.CreateAsync(new IdentityRole<int>(rol));
        }

        // Crear usuario administrador inicial
        const string emailAdmin = "admin@zentinela.com";
        if (await userManager.FindByEmailAsync(emailAdmin) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = emailAdmin,
                Email = emailAdmin,
                NombreCompleto = "Administrador",
                EmailConfirmed = true,
                LiderID = null
            };

            var resultado = await userManager.CreateAsync(admin, "Admin1234!");
            if (resultado.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}

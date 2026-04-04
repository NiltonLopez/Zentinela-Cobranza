using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Models;

namespace Zentinela.Infrastructure.SQLServer.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Roles definidos como constantes en Application — fuente única de verdad
        string[] roles = [Roles.Admin, Roles.Lider, Roles.Director, Roles.Asesor];
        foreach (var rol in roles)
        {
            if (!await roleManager.RoleExistsAsync(rol))
                await roleManager.CreateAsync(new IdentityRole<int>(rol));
        }

        const string emailAdmin = "admin@zentinela.com";
        if (await userManager.FindByEmailAsync(emailAdmin) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = emailAdmin,
                Email = emailAdmin,
                NombreCompleto = "Administrador",
                EmailConfirmed = true,
                LiderID = null,
            };
            var resultado = await userManager.CreateAsync(admin, "Admin1234!");
            if (resultado.Succeeded)
                await userManager.AddToRoleAsync(admin, Roles.Admin);
        }
    }
}

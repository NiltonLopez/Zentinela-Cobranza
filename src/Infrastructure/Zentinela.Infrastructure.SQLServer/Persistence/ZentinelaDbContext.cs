using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Zentinela.Infrastructure.SQLServer.Mappings;
using Zentinela.Infrastructure.SQLServer.Models;

namespace Zentinela.Infrastructure.SQLServer.Persistence;

/// <summary>
/// Contexto propietario de Zentinela → DBZentinela.
/// Contiene Identity y tablas exclusivas de la aplicación.
/// Las migraciones de EF Core corren SOLO contra esta BD.
/// </summary>
public class ZentinelaDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public ZentinelaDbContext(DbContextOptions<ZentinelaDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ApplicationUserMapping());
    }
}

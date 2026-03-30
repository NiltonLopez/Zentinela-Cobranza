using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Zentinela.API.Domain.Entities;

namespace Zentinela.API.Infrastructure.Data;

public class ZentinelaDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public ZentinelaDbContext(DbContextOptions<ZentinelaDbContext> options) : base(options) { }

    public DbSet<Lider> Lideres { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Mapear ApplicationUser al esquema existente de Identity
        builder.Entity<ApplicationUser>(e =>
        {
            e.ToTable("AspNetUsers");
            e.HasOne<Lider>()
             .WithOne(l => l.Usuario)
             .HasForeignKey<ApplicationUser>(u => u.LiderID)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // Mapear tabla Lider existente en BD — excluir de migraciones (ya existe en producción)
        builder.Entity<Lider>(e =>
        {
            e.ToTable("Lider");
            e.HasKey(l => l.ID);
            e.Property(l => l.Nombre).HasMaxLength(100).IsRequired();
            e.ToTable("Lider", t => t.ExcludeFromMigrations());
        });
    }
}

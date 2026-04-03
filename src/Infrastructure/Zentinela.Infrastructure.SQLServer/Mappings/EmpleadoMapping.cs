using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class EmpleadoMapping : IEntityTypeConfiguration<Empleado>
{
    public void Configure(EntityTypeBuilder<Empleado> builder)
    {
        builder.ToTable("Empleado", t => t.ExcludeFromMigrations());
        builder.HasKey(emp => emp.ID);
        builder.Property(emp => emp.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(emp => emp.Apellidos).HasMaxLength(100).IsRequired();
        builder.Property(emp => emp.Ciudad).HasMaxLength(100).IsRequired();
        builder.HasOne(emp => emp.Cargo)
               .WithMany()
               .HasForeignKey(emp => emp.CargoID);
        // Relación recursiva: empleado → su supervisor
        builder.HasOne(emp => emp.Supervisor)
               .WithMany()
               .HasForeignKey(emp => emp.IDSupervisor)
               .IsRequired(false);
    }
}

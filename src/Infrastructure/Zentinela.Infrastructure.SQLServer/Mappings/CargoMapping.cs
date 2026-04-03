using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class CargoMapping : IEntityTypeConfiguration<Cargo>
{
    public void Configure(EntityTypeBuilder<Cargo> builder)
    {
        builder.ToTable("Cargo", t => t.ExcludeFromMigrations());
        builder.HasKey(c => c.ID);
        builder.Property(c => c.Descripcion).HasMaxLength(100).IsRequired();
    }
}

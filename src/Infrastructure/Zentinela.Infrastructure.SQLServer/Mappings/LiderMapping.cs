using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class LiderMapping : IEntityTypeConfiguration<Lider>
{
    public void Configure(EntityTypeBuilder<Lider> builder)
    {
        builder.ToTable("Lider", t => t.ExcludeFromMigrations());
        builder.HasKey(l => l.ID);
        builder.Property(l => l.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Email).HasMaxLength(150);
    }
}

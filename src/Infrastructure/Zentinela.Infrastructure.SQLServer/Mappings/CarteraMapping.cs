using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class CarteraMapping : IEntityTypeConfiguration<Cartera>
{
    public void Configure(EntityTypeBuilder<Cartera> builder)
    {
        builder.ToTable("Cartera", t => t.ExcludeFromMigrations());
        builder.HasKey(c => c.ID);
        builder.Property(c => c.NombreCartera).HasMaxLength(100).IsRequired();
    }
}

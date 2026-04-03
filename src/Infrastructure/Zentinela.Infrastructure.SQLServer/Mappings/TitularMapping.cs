using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class TitularMapping : IEntityTypeConfiguration<Titular>
{
    public void Configure(EntityTypeBuilder<Titular> builder)
    {
        builder.ToTable("Titular", t => t.ExcludeFromMigrations());
        builder.HasKey(t => t.NumeroDocumento);
        builder.Property(t => t.Nombre).HasMaxLength(70).IsRequired();
    }
}

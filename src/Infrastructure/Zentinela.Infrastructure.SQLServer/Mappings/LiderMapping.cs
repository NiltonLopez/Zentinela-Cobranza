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
        // Lider.ID NO es IDENTITY en DBCallCenter: el ID se asigna manualmente
        // al crear un líder (MAX(ID)+1), igual que Cartera. Sin esto, EF asume
        // generación por el store y omite el valor en el INSERT (fallaría).
        builder.Property(l => l.ID).ValueGeneratedNever();
        builder.Property(l => l.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Email).HasMaxLength(150);
        builder.Property(l => l.NumeroDocumento);
    }
}

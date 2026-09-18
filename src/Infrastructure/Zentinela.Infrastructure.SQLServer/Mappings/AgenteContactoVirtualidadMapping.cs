using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class AgenteContactoVirtualidadMapping : IEntityTypeConfiguration<AgenteContactoVirtualidad>
{
    public void Configure(EntityTypeBuilder<AgenteContactoVirtualidad> builder)
    {
        builder.ToTable("AgenteContactoVirtualidad", t => t.ExcludeFromMigrations());
        builder.HasKey(c => c.AgenteID);

        builder.Property(c => c.AgenteID).ValueGeneratedNever();
        builder.Property(c => c.NumeroCorporativo).HasMaxLength(10);
        builder.Property(c => c.LinkWhatsapp).HasMaxLength(100);
        builder.Property(c => c.FechaActualizacion).HasColumnType("datetime2").HasDefaultValueSql("GETDATE()");
        builder.Property(c => c.UsuarioActualizacion).HasMaxLength(100).IsRequired();

        builder.HasOne<Agente>()
            .WithOne()
            .HasForeignKey<AgenteContactoVirtualidad>(c => c.AgenteID)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

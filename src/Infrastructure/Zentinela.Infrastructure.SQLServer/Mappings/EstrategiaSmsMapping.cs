using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class EstrategiaSmsMapping : IEntityTypeConfiguration<EstrategiaSms>
{
    public void Configure(EntityTypeBuilder<EstrategiaSms> builder)
    {
        builder.ToTable("EstrategiaSms", t => t.ExcludeFromMigrations());
        builder.HasKey(e => e.ID);

        builder.Property(e => e.ID).ValueGeneratedOnAdd();
        builder.Property(e => e.UsuarioGenero).HasMaxLength(100).IsRequired();
        builder.Property(e => e.FechaGeneracion).HasColumnType("datetime2").HasDefaultValueSql("GETDATE()");
        builder.Property(e => e.HuellaArchivo).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(e => e.NombreArchivo).HasMaxLength(255).IsRequired();

        builder.HasOne<Lider>()
            .WithMany()
            .HasForeignKey(e => e.LiderID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MensajeAutorizado>()
            .WithMany()
            .HasForeignKey(e => e.MensajeAutorizadoID)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

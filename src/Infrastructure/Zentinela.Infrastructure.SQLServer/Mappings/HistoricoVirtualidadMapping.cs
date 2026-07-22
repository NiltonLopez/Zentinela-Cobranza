using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class HistoricoVirtualidadMapping : IEntityTypeConfiguration<HistoricoVirtualidad>
{
    public void Configure(EntityTypeBuilder<HistoricoVirtualidad> builder)
    {
        // La BD es propiedad del sistema de cobranza; el esquema se gestiona por
        // migración SQL manual (docs/sql), no por EF Migrations.
        builder.ToTable("HistoricoVirtualidad", t => t.ExcludeFromMigrations());
        builder.HasKey(s => s.ID);

        builder.Property(s => s.ID).ValueGeneratedOnAdd();
        builder.Property(s => s.Canal).HasMaxLength(10).IsRequired();
        builder.Property(s => s.NumeroDocumento).IsRequired();
        builder.Property(s => s.Destino).HasMaxLength(255).IsRequired();
        builder.Property(s => s.CarteraID).IsRequired();
        builder.Property(s => s.LiderID).IsRequired();
        builder.Property(s => s.FechaEnvio).HasColumnType("date").IsRequired();
        builder.Property(s => s.Estado).HasMaxLength(50); // nullable = sin resultado
        builder.Property(s => s.FechaResultado).HasColumnType("datetime2");
        builder.Property(s => s.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();

        builder.HasOne<LiderCartera>()
            .WithMany()
            .HasForeignKey(s => new { s.LiderID, s.CarteraID })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

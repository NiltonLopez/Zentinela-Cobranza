using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

// HasNoKey + HasOne con FK compuesta requiere que la entidad referenciada (LiderCartera)
// también esté mapeada en el DbContext (ya lo está como tabla independiente).

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class MensajeriaEnviadaMapping : IEntityTypeConfiguration<MensajeriaEnviada>
{
    public void Configure(EntityTypeBuilder<MensajeriaEnviada> builder)
    {
        builder.ToTable("MensajesEnviados", t => t.ExcludeFromMigrations());
        builder.HasNoKey();

        builder.Property(m => m.NumeroDocumento).IsRequired();
        builder.Property(m => m.NumeroTelefono).HasMaxLength(20).IsRequired();
        builder.Property(m => m.LiderID).IsRequired();
        builder.Property(m => m.CarteraID).IsRequired();
        builder.Property(m => m.FechaEnvio).HasColumnType("date").IsRequired();
        builder.Property(m => m.Canal).HasMaxLength(20).IsRequired();
        builder.Property(m => m.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();

        // FK compuesta a Lider_Cartera (LiderID, CarteraID)
        builder.HasOne<LiderCartera>()
            .WithMany()
            .HasForeignKey(m => new { m.LiderID, m.CarteraID })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

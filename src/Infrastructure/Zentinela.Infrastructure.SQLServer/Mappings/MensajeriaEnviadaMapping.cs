using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class MensajeriaEnviadaMapping : IEntityTypeConfiguration<MensajeriaEnviada>
{
    public void Configure(EntityTypeBuilder<MensajeriaEnviada> builder)
    {
        builder.ToTable("MensajeriaEnviada", t => t.ExcludeFromMigrations());

        builder.HasKey(m => m.MensajeriaID);
        builder.Property(m => m.MensajeriaID).UseIdentityColumn();
        builder.Property(m => m.NumeroDocumento).IsRequired();
        builder.Property(m => m.NumeroTelefono).HasMaxLength(20).IsRequired();
        builder.Property(m => m.NombreCartera).HasMaxLength(100).IsRequired();
        builder.Property(m => m.CarteraID);
        builder.Property(m => m.FechaEnvio).HasColumnType("date").IsRequired();
        builder.Property(m => m.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
    }
}

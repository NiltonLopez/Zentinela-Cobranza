using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class IVREnviadoMapping : IEntityTypeConfiguration<IVREnviado>
{
    public void Configure(EntityTypeBuilder<IVREnviado> builder)
    {
        builder.ToTable("IVREnviados", t => t.ExcludeFromMigrations());
        builder.HasNoKey();

        builder.Property(i => i.NumeroDocumento).IsRequired();
        builder.Property(i => i.NumeroTelefono).HasMaxLength(20).IsRequired();
        builder.Property(i => i.LiderID).IsRequired();
        builder.Property(i => i.CarteraID).IsRequired();
        builder.Property(i => i.FechaEnvio).HasColumnType("date").IsRequired();
        builder.Property(i => i.Canal).HasMaxLength(20).IsRequired();
        builder.Property(i => i.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();

        builder.HasOne<LiderCartera>()
            .WithMany()
            .HasForeignKey(i => new { i.LiderID, i.CarteraID })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

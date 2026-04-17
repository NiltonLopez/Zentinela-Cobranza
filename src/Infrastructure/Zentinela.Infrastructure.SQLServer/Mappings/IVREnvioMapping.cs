using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class IVREnvioMapping : IEntityTypeConfiguration<IVREnvio>
{
    public void Configure(EntityTypeBuilder<IVREnvio> builder)
    {
        builder.ToTable("IVREnvio", t => t.ExcludeFromMigrations());

        builder.HasKey(i => i.IVRID);
        builder.Property(i => i.IVRID).UseIdentityColumn();
        builder.Property(i => i.TitularID).IsRequired();
        builder.Property(i => i.Celular).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Campana).HasMaxLength(100);
        builder.Property(i => i.CarteraID);
        builder.Property(i => i.FechaEnvio).HasColumnType("date").IsRequired();
        builder.Property(i => i.Resultado).HasMaxLength(50);
        builder.Property(i => i.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
        builder.Property(i => i.FechaResultado).HasColumnType("datetime2");
    }
}

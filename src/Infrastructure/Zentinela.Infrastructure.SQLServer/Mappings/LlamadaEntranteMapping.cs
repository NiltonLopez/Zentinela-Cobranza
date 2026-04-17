using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class LlamadaEntranteMapping : IEntityTypeConfiguration<LlamadaEntrante>
{
    public void Configure(EntityTypeBuilder<LlamadaEntrante> builder)
    {
        builder.ToTable("LlamadaEntrante", t => t.ExcludeFromMigrations());

        builder.HasKey(l => l.LlamadaEntranteID);
        builder.Property(l => l.LlamadaEntranteID).UseIdentityColumn();
        builder.Property(l => l.Celular).HasMaxLength(20).IsRequired();
        builder.Property(l => l.TitularID);
        builder.Property(l => l.CarteraID);
        builder.Property(l => l.TieneAcuerdo);
        builder.Property(l => l.Semana).IsRequired();
        builder.Property(l => l.Anio).IsRequired();
        builder.Property(l => l.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
    }
}

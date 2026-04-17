using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class LlamadaPerdidaMapping : IEntityTypeConfiguration<LlamadaPerdida>
{
    public void Configure(EntityTypeBuilder<LlamadaPerdida> builder)
    {
        builder.ToTable("LlamadaPerdida", t => t.ExcludeFromMigrations());

        builder.HasKey(l => l.LlamadaPerdidaID);
        builder.Property(l => l.LlamadaPerdidaID).UseIdentityColumn();
        builder.Property(l => l.TitularID).IsRequired();
        builder.Property(l => l.Celular).HasMaxLength(20).IsRequired();
        builder.Property(l => l.CarteraID).IsRequired();
        builder.Property(l => l.FechaCargue)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
    }
}

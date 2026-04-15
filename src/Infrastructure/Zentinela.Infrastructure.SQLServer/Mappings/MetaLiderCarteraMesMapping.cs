using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class MetaLiderCarteraMesMapping : IEntityTypeConfiguration<MetaLiderCarteraMes>
{
    public void Configure(EntityTypeBuilder<MetaLiderCarteraMes> builder)
    {
        builder.ToTable("Meta_Lider_Cartera_Mes", t => t.ExcludeFromMigrations());
        builder.HasKey(m => m.MetaID);
        builder.Property(m => m.MetaID).UseIdentityColumn();
        builder.Property(m => m.LiderID).IsRequired();
        builder.Property(m => m.CarteraID).IsRequired();
        builder.Property(m => m.Año).IsRequired();
        builder.Property(m => m.Mes).IsRequired();
        builder.Property(m => m.MetaRecaudo).HasColumnType("decimal(18,2)").IsRequired();
    }
}

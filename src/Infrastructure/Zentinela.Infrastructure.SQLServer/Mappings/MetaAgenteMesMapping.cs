using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class MetaAgenteMesMapping : IEntityTypeConfiguration<MetaAgenteMes>
{
    public void Configure(EntityTypeBuilder<MetaAgenteMes> builder)
    {
        builder.ToTable("Meta_Agente_Mes", t => t.ExcludeFromMigrations());
        builder.HasKey(m => m.MetaID);
        builder.Property(m => m.MetaID).UseIdentityColumn();
        builder.Property(m => m.AgenteID).IsRequired();
        builder.Property(m => m.Año).IsRequired();
        builder.Property(m => m.Mes).IsRequired();
        builder.Property(m => m.MetaRecaudo).HasColumnType("decimal(18,2)").IsRequired();
    }
}

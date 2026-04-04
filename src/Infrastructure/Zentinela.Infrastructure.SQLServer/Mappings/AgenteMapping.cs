using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class AgenteMapping : IEntityTypeConfiguration<Agente>
{
    public void Configure(EntityTypeBuilder<Agente> builder)
    {
        builder.ToTable("Agente", t => t.ExcludeFromMigrations());
        builder.HasKey(a => a.AgenteID);
        builder.Property(a => a.UsuarioControlNext).HasMaxLength(100);
        builder.Property(a => a.Activo).IsRequired();
    }
}

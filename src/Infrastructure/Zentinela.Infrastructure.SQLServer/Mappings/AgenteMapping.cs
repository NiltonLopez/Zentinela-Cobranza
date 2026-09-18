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

        // Columnas agregadas el 2026-09-18 (docs/sql/migrations/2026-09-18_contacto_virtualidad_en_agente.sql).
        builder.Property(a => a.NumeroCorporativo).HasMaxLength(10);
        builder.Property(a => a.LinkWhatsapp).HasMaxLength(100);
        builder.Property(a => a.ContactoActualizadoEn).HasColumnType("datetime2");
        builder.Property(a => a.ContactoActualizadoPor).HasMaxLength(100);
    }
}

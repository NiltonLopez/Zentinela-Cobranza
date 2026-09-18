using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class MensajeAutorizadoMapping : IEntityTypeConfiguration<MensajeAutorizado>
{
    public void Configure(EntityTypeBuilder<MensajeAutorizado> builder)
    {
        // Esquema por script SQL manual (docs/sql/migrations/2026-09-18_creador_virtualidad_sms.sql).
        builder.ToTable("MensajeAutorizado", t => t.ExcludeFromMigrations());
        builder.HasKey(m => m.ID);

        builder.Property(m => m.ID).ValueGeneratedOnAdd();
        builder.Property(m => m.Titulo).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Texto).HasMaxLength(320).IsRequired();
        builder.Property(m => m.Activo).IsRequired();
        builder.Property(m => m.FechaCreacion).HasColumnType("datetime2").HasDefaultValueSql("GETDATE()");
        builder.Property(m => m.UsuarioCreacion).HasMaxLength(100).IsRequired();
        builder.Property(m => m.FechaActualizacion).HasColumnType("datetime2").HasDefaultValueSql("GETDATE()");
        builder.Property(m => m.UsuarioActualizacion).HasMaxLength(100).IsRequired();
    }
}

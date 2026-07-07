using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class HistoricoEstadoTitularMapping : IEntityTypeConfiguration<HistoricoEstadoTitular>
{
    public void Configure(EntityTypeBuilder<HistoricoEstadoTitular> builder)
    {
        builder.ToTable("HistoricoEstadoTitular", t => t.ExcludeFromMigrations());
        // Entidad de solo lectura (el cargue la escribe vía SqlBulkCopy/MERGE, no por EF).
        // Keyless porque AgenteID puede ser NULL en la BD y no se necesita seguimiento.
        builder.HasNoKey();
        builder.Property(h => h.UltimoPerfil).HasMaxLength(50);
        builder.Property(h => h.MejorPerfil).HasMaxLength(50);
        builder.Property(h => h.NumeroMarcado).HasMaxLength(10);
        builder.Property(h => h.MotivoNoPago).HasMaxLength(100);
        builder.Property(h => h.SaldoTotal).HasColumnType("money");
        builder.Property(h => h.ValorPromesa).HasColumnType("money");
        builder.Property(h => h.FechaActualizacion).IsRequired();
    }
}

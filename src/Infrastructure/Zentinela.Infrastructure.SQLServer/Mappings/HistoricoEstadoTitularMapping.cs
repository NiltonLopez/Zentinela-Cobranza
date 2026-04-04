using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class HistoricoEstadoTitularMapping : IEntityTypeConfiguration<HistoricoEstadoTitular>
{
    public void Configure(EntityTypeBuilder<HistoricoEstadoTitular> builder)
    {
        builder.ToTable("HistoricoEstadoTitular", t => t.ExcludeFromMigrations());
        builder.HasKey(h => new { h.TitularID, h.CarteraID, h.AgenteID });
        builder.Property(h => h.UltimoPerfil).HasMaxLength(50);
        builder.Property(h => h.MejorPerfil).HasMaxLength(50);
        builder.Property(h => h.NumeroMarcado).HasMaxLength(10);
        builder.Property(h => h.MotivoNoPago).HasMaxLength(100);
        builder.Property(h => h.SaldoTotal).HasColumnType("money");
        builder.Property(h => h.ValorPromesa).HasColumnType("money");
    }
}

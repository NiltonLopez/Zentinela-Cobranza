using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class UltimoEstadoTitularMapping : IEntityTypeConfiguration<UltimoEstadoTitular>
{
    public void Configure(EntityTypeBuilder<UltimoEstadoTitular> builder)
    {
        builder.ToTable("UltimoEstadoTitular", t => t.ExcludeFromMigrations());
        // PK real en BD es (TitularID, CarteraID); AgenteID es un campo nullable
        // que se actualiza vía MERGE en CargueReporteService.
        builder.HasKey(u => new { u.TitularID, u.CarteraID });
        builder.Property(u => u.UltimoPerfil).HasMaxLength(50);
        builder.Property(u => u.MejorPerfil).HasMaxLength(50);
        builder.Property(u => u.NumeroMarcado).HasMaxLength(10);
        builder.Property(u => u.MotivoNoPago).HasMaxLength(100);
        builder.Property(u => u.SaldoTotal).HasColumnType("money");
        builder.Property(u => u.ValorPromesa).HasColumnType("money");
        builder.Property(u => u.FechaActualizacion).IsRequired();

        builder.HasOne(u => u.Titular)
               .WithMany()
               .HasForeignKey(u => u.TitularID)
               .HasPrincipalKey(t => t.NumeroDocumento);
    }
}

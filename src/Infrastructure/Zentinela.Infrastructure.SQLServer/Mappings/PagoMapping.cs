using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

/// <summary>
/// Mapeo de la entidad Pago a la tabla Pago en DBCallCenter.
/// ExcludeFromMigrations porque DBCallCenter no es gestionada por Zentinela.
/// El script de creación está en docs/migrations/create_Pago_table.sql.
/// </summary>
public class PagoMapping : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> builder)
    {
        builder.ToTable("Pago", t => t.ExcludeFromMigrations());

        builder.HasKey(p => p.PagoID);

        builder.Property(p => p.PagoID).UseIdentityColumn();
        builder.Property(p => p.CedulaTitular).IsRequired();
        builder.Property(p => p.NombreCliente).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ValorPagado).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.FechaPago).HasColumnType("date").IsRequired();
        builder.Property(p => p.Honorarios).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.LiderID).IsRequired();
        builder.Property(p => p.CarteraID).IsRequired();
        // UsuarioRegistro guarda el UserName (no el Id); ver migración 2026-05-27_pago_usuarioregistro.sql
        builder.Property(p => p.UsuarioRegistro).HasMaxLength(256).IsRequired();
        // AgenteControlID nullable: ver migración 2026-05-21_pago_agentecontrolid_nullable.sql
        builder.Property(p => p.AgenteControlID);
        builder.Property(p => p.FechaRegistro)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()")
            .IsRequired();
    }
}

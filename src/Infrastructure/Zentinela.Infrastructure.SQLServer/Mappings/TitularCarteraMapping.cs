using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

/// <summary>
/// Titular_Cartera — pivote principal del negocio.
/// </summary>
public class TitularCarteraMapping : IEntityTypeConfiguration<TitularCartera>
{
    public void Configure(EntityTypeBuilder<TitularCartera> builder)
    {
        builder.ToTable("Titular_Cartera", t => t.ExcludeFromMigrations());
        builder.HasKey(tc => new { tc.TitularID, tc.LiderID, tc.CarteraID });
        builder.HasOne(tc => tc.Titular)
               .WithMany()
               .HasForeignKey(tc => tc.TitularID);
        builder.HasOne(tc => tc.Lider)
               .WithMany()
               .HasForeignKey(tc => tc.LiderID);
        builder.HasOne(tc => tc.Cartera)
               .WithMany()
               .HasForeignKey(tc => tc.CarteraID);
    }
}

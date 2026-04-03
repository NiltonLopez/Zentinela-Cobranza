using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

/// <summary>
/// Lider_Cartera — pivote M:N. FK hacia Cartera se llama CartID en BD.
/// </summary>
public class LiderCarteraMapping : IEntityTypeConfiguration<LiderCartera>
{
    public void Configure(EntityTypeBuilder<LiderCartera> builder)
    {
        builder.ToTable("Lider_Cartera", t => t.ExcludeFromMigrations());
        builder.HasKey(lc => new { lc.LiderID, lc.CartID });
        builder.HasOne(lc => lc.Lider)
               .WithMany()
               .HasForeignKey(lc => lc.LiderID);
        builder.HasOne(lc => lc.Cartera)
               .WithMany()
               .HasForeignKey(lc => lc.CartID);
    }
}

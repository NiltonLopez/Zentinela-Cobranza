using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class CarteraAgenteMapping : IEntityTypeConfiguration<CarteraAgente>
{
    public void Configure(EntityTypeBuilder<CarteraAgente> builder)
    {
        builder.ToTable("Cartera_Agente");
        builder.HasKey(ca => new { ca.AgenteID, ca.LiderID, ca.CarteraID });

        builder.Property(ca => ca.AgenteID).HasColumnName("AgenteID");
        builder.Property(ca => ca.LiderID).HasColumnName("LiderID");
        builder.Property(ca => ca.CarteraID).HasColumnName("CarteraID");
    }
}

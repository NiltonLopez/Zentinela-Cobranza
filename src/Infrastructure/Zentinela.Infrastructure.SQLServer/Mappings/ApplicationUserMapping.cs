using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Infrastructure.SQLServer.Models;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

public class ApplicationUserMapping : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("AspNetUsers");
        builder.Property(u => u.NombreCompleto).IsRequired();
    }
}

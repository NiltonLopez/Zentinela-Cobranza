using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

/// <summary>
/// <c>DBRecursosHumanos.dbo.FotoEmpleado</c>, solo lectura. Tipos verificados con
/// <c>sys.columns</c> el 2026-09-25: ID int identity (PK), IDEmpleado int (único), FileName
/// nvarchar(255), ContentType nvarchar(100), FileContent varbinary(max), UploadedBy nvarchar(150),
/// UploadedAt datetime2; todas NOT NULL.
/// </summary>
public class FotoEmpleadoMapping : IEntityTypeConfiguration<FotoEmpleado>
{
    public void Configure(EntityTypeBuilder<FotoEmpleado> builder)
    {
        builder.ToTable("FotoEmpleado", t => t.ExcludeFromMigrations());
        builder.HasKey(f => f.ID);
        builder.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.FileContent).IsRequired();
        builder.Property(f => f.UploadedBy).HasMaxLength(150).IsRequired();
        builder.Property(f => f.UploadedAt).HasColumnType("datetime2");
        builder.HasIndex(f => f.IDEmpleado).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Mappings;

namespace Zentinela.Infrastructure.SQLServer.Persistence;

/// <summary>
/// Contexto de lectura / escritura puntual contra DBRecursosHumanos.
/// Todas las entidades usan ExcludeFromMigrations — la BD es propiedad de RRHH, no de Zentinela.
/// </summary>
public class RecursosHumanosDbContext : DbContext
{
    public RecursosHumanosDbContext(DbContextOptions<RecursosHumanosDbContext> options) : base(options) { }

    public DbSet<Empleado> Empleados { get; set; }
    public DbSet<Cargo> Cargos { get; set; }

    /// <summary>Fotos de empleados (solo lectura; las sube RRHH).</summary>
    public DbSet<FotoEmpleado> FotosEmpleado { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new CargoMapping());
        builder.ApplyConfiguration(new EmpleadoMapping());
        builder.ApplyConfiguration(new FotoEmpleadoMapping());
    }
}

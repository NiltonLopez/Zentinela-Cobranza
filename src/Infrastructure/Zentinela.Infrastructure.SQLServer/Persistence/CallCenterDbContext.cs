using Microsoft.EntityFrameworkCore;
using Zentinela.Core.Entities;
using Zentinela.Infrastructure.SQLServer.Mappings;

namespace Zentinela.Infrastructure.SQLServer.Persistence;

/// <summary>
/// Contexto de solo lectura / escritura puntual contra DBCallCenter.
/// Todas las entidades usan ExcludeFromMigrations — la BD es propiedad del sistema de cobranza, no de Zentinela.
/// </summary>
public class CallCenterDbContext : DbContext
{
    public CallCenterDbContext(DbContextOptions<CallCenterDbContext> options) : base(options) { }

    public DbSet<Lider> Lideres { get; set; }
    public DbSet<Cartera> Carteras { get; set; }
    public DbSet<LiderCartera> LiderCarteras { get; set; }
    public DbSet<Titular> Titulares { get; set; }
    public DbSet<TitularCartera> TitularCarteras { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new LiderMapping());
        builder.ApplyConfiguration(new CarteraMapping());
        builder.ApplyConfiguration(new LiderCarteraMapping());
        builder.ApplyConfiguration(new TitularMapping());
        builder.ApplyConfiguration(new TitularCarteraMapping());
    }
}

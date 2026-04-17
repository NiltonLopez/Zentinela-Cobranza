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

    public DbSet<Agente> Agentes { get; set; }
    public DbSet<Lider> Lideres { get; set; }
    public DbSet<Cartera> Carteras { get; set; }
    public DbSet<LiderCartera> LiderCarteras { get; set; }
    public DbSet<Titular> Titulares { get; set; }
    public DbSet<TitularCartera> TitularCarteras { get; set; }
    public DbSet<UltimoEstadoTitular> UltimosEstadosTitular { get; set; }
    public DbSet<HistoricoEstadoTitular> HistoricoEstadosTitular { get; set; }
    public DbSet<BaseMadreRow> BaseMadreRows { get; set; }
    public DbSet<CarteraAgente> CarteraAgentes { get; set; }
    public DbSet<Pago>                Pagos               { get; set; }
    public DbSet<MetaAgenteMes>       MetaAgentesMes      { get; set; }
    public DbSet<MetaLiderCarteraMes> MetaLiderCarteraMes { get; set; }
    public DbSet<MensajeriaEnviada>   MensajeriaEnviadas  { get; set; }
    public DbSet<IVREnvio>            IVREnvios           { get; set; }
    public DbSet<LlamadaPerdida>      LlamadasPerdidas    { get; set; }
    public DbSet<LlamadaEntrante>     LlamadasEntrantes   { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new AgenteMapping());
        builder.ApplyConfiguration(new LiderMapping());
        builder.ApplyConfiguration(new CarteraMapping());
        builder.ApplyConfiguration(new LiderCarteraMapping());
        builder.ApplyConfiguration(new TitularMapping());
        builder.ApplyConfiguration(new TitularCarteraMapping());
        builder.ApplyConfiguration(new UltimoEstadoTitularMapping());
        builder.ApplyConfiguration(new HistoricoEstadoTitularMapping());
        builder.ApplyConfiguration(new BaseMadreRowMapping());
        builder.ApplyConfiguration(new CarteraAgenteMapping());
        builder.ApplyConfiguration(new PagoMapping());
        builder.ApplyConfiguration(new MetaAgenteMesMapping());
        builder.ApplyConfiguration(new MetaLiderCarteraMesMapping());
        builder.ApplyConfiguration(new MensajeriaEnviadaMapping());
        builder.ApplyConfiguration(new IVREnvioMapping());
        builder.ApplyConfiguration(new LlamadaPerdidaMapping());
        builder.ApplyConfiguration(new LlamadaEntranteMapping());
    }
}

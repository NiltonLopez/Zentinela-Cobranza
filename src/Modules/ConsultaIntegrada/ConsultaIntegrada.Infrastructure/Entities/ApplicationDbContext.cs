using Microsoft.EntityFrameworkCore;

namespace ConsultaIntegrada.Infrastructure.Entities;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext() { }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public virtual DbSet<Titular> Titulars { get; set; }
    public virtual DbSet<Cartera> Carteras { get; set; }
    public virtual DbSet<Lider> Liders { get; set; }
    public virtual DbSet<TitularCartera> TitularCarteras { get; set; }

    public virtual DbSet<Arlsura> Arlsuras { get; set; }
    public virtual DbSet<Cajacopi> Cajacopis { get; set; }
    public virtual DbSet<Camara> Camaras { get; set; }
    public virtual DbSet<Fosyga> Fosygas { get; set; }
    public virtual DbSet<Positiva> Positivas { get; set; }
    public virtual DbSet<PositivaEmpresa> PositivaEmpresas { get; set; }
    public virtual DbSet<Rgm> Rgms { get; set; }
    public virtual DbSet<Rue> Rues { get; set; }
    public virtual DbSet<SaludTotal> SaludTotals { get; set; }
    public virtual DbSet<Simit> Simits { get; set; }
    public virtual DbSet<So> Sos { get; set; }
    public virtual DbSet<Tigo> Tigos { get; set; }
    public virtual DbSet<DatosClaro> DatosClaros { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Titular_Cartera — pivote 3-way
        modelBuilder.Entity<TitularCartera>(entity =>
        {
            entity.HasKey(tc => new { tc.TitularId, tc.LiderId, tc.CarteraId });

            entity.HasOne(tc => tc.Titular)
                  .WithMany()
                  .HasForeignKey(tc => tc.TitularId);

            entity.HasOne(tc => tc.Lider)
                  .WithMany()
                  .HasForeignKey(tc => tc.LiderId);

            entity.HasOne(tc => tc.Cartera)
                  .WithMany()
                  .HasForeignKey(tc => tc.CarteraId);
        });

        // Entidades Keyless — FK por convención sobre TitularID
        modelBuilder.Entity<Arlsura>().HasNoKey().ToTable("ARLSura");
        modelBuilder.Entity<Cajacopi>().HasNoKey().ToTable("Cajacopi");
        modelBuilder.Entity<Camara>().HasNoKey().ToTable("Camara");
        modelBuilder.Entity<Fosyga>().HasNoKey().ToTable("Fosyga");
        modelBuilder.Entity<Positiva>().HasNoKey().ToTable("Positiva");
        modelBuilder.Entity<PositivaEmpresa>().HasNoKey();
        modelBuilder.Entity<Rgm>().HasNoKey().ToTable("RGM");
        modelBuilder.Entity<Rue>().HasNoKey().ToTable("RUES");
        modelBuilder.Entity<SaludTotal>().HasNoKey().ToTable("SaludTotal");
        modelBuilder.Entity<Simit>().HasNoKey().ToTable("SIMIT");
        modelBuilder.Entity<So>().HasNoKey().ToTable("SOS");
        modelBuilder.Entity<Tigo>().HasNoKey().ToTable("Tigo");
        modelBuilder.Entity<DatosClaro>().HasNoKey().ToTable("DatosClaro");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}

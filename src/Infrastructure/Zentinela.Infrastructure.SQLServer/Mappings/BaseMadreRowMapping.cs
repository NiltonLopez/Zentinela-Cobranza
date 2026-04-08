using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentinela.Core.Entities;

namespace Zentinela.Infrastructure.SQLServer.Mappings;

/// <summary>
/// Mapea BaseMadreRow a la vista vw_BaseMadre.
/// Los nombres de columna en BD tienen espacios → se definen explícitamente con HasColumnName.
/// ExcludeFromMigrations: la vista pertenece a DBCallCenter, no a Zentinela.
/// </summary>
public class BaseMadreRowMapping : IEntityTypeConfiguration<BaseMadreRow>
{
    public void Configure(EntityTypeBuilder<BaseMadreRow> builder)
    {
        builder.HasNoKey().ToView("vw_BaseMadre");

        // CarteraID y LiderID coinciden con el nombre en BD
        builder.Property(b => b.CarteraID).HasColumnName("CarteraID");
        builder.Property(b => b.LiderID).HasColumnName("LiderID");

        // Columnas cuyo nombre en BD difiere del nombre de la propiedad C#
        builder.Property(b => b.NumeroDocumento)         .HasColumnName("Numero Documento");
        builder.Property(b => b.UltimaFechaGestion)      .HasColumnName("Ultima Fecha Gestion");
        builder.Property(b => b.FechaPromesa)            .HasColumnName("Fecha Promesa");
        builder.Property(b => b.ValorPromesa)            .HasColumnName("Valor Promesa");
        builder.Property(b => b.MejorPerfil)             .HasColumnName("Mejor Perfil");
        builder.Property(b => b.UltimoPerfil)            .HasColumnName("Ultimo Perfil");
        builder.Property(b => b.UltimoNumeroMarcado)     .HasColumnName("Ultimo Numero Marcado en ControlNext");
        builder.Property(b => b.EstadoEnFosyga)          .HasColumnName("Estado en Fosyga");
        builder.Property(b => b.TipoAfiliado)            .HasColumnName("Tipo Afiliado");
        builder.Property(b => b.UltimaConsultaFosyga)    .HasColumnName("Ultima Consulta en Fosyga");
        builder.Property(b => b.EPSEnCompensados)        .HasColumnName("EPS en Compensados");
        builder.Property(b => b.UltimoPeriodoCompensado) .HasColumnName("Ultimo Periodo Compensado");
        builder.Property(b => b.TipoAfiliadoCompensados) .HasColumnName("Tipo Afiliado Compensados");
        builder.Property(b => b.UltimaConsultaCompensados).HasColumnName("Ultima Consulta en Compensados");
        builder.Property(b => b.EdadRegistraduria)       .HasColumnName("Edad Registraduria");
        builder.Property(b => b.EdadCajaCopi)            .HasColumnName("Edad CajaCopi");
        builder.Property(b => b.EdadPositiva)            .HasColumnName("Edad Positiva");
        builder.Property(b => b.CelularCajacopi)         .HasColumnName("Celular Cajacopi");
        builder.Property(b => b.EmailCajacopi)           .HasColumnName("Email CajaCopi");
        builder.Property(b => b.CelularCamara)           .HasColumnName("Celular Camara");
        builder.Property(b => b.EmailCamara)             .HasColumnName("Email Camara");
        builder.Property(b => b.CelularSaludTotal)       .HasColumnName("Celular Salud Total");
        builder.Property(b => b.EmailSaludTotal)         .HasColumnName("Email Salud Total");
        builder.Property(b => b.CelularDatosExternos)    .HasColumnName("Celular Datos Externos");
        builder.Property(b => b.EmailDatosExternos)      .HasColumnName("Email Datos Externos");
        builder.Property(b => b.CelularPositiva)         .HasColumnName("Celular Positiva");
        builder.Property(b => b.EmailPositiva)           .HasColumnName("Email Positiva");
        builder.Property(b => b.CelularTigo)             .HasColumnName("Celular Tigo");
        builder.Property(b => b.EmailTigo)               .HasColumnName("Email Tigo");
        builder.Property(b => b.CelularSOS)              .HasColumnName("Celular SOS");
        builder.Property(b => b.NITSura)                 .HasColumnName("NIT Sura");
        builder.Property(b => b.EmpresaSura)             .HasColumnName("Empresa Sura");
        builder.Property(b => b.NITPositiva)             .HasColumnName("NIT Positiva");
        builder.Property(b => b.EmpresaPositiva)         .HasColumnName("Empresa Positiva");
        builder.Property(b => b.NITSaludTotal)           .HasColumnName("NIT Salud Total");
        builder.Property(b => b.EmpresaSaludTotal)       .HasColumnName("Empresa Salud Total");
        builder.Property(b => b.NitSOS)                  .HasColumnName("Nit Sos");
        builder.Property(b => b.EmpresaSOS)              .HasColumnName("Empresa SOS");
        builder.Property(b => b.EmpresaRUES)             .HasColumnName("Empresa RUES");
        builder.Property(b => b.PlacaSIMIT)              .HasColumnName("PLACA SIMIT");
        // Nombre, Saldo, Rotacion, EPS, Regimen, Departamento, Municipio,
        // Marca, Modelo, Placa → coinciden con el nombre de la columna en BD
    }
}

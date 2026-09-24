using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Traducción de sp_ProcesarCamaraAuxiliar: indicativo a los fijos de 7 dígitos (por
/// departamento o municipio, con PrefijoMunicipio + PrefijosDepartamento) y textos de relleno
/// del formulario en la localización.
/// </summary>
public class ReglaCamaraTests
{
    private readonly ReglaCamara _regla = new();

    // Como lo arma GeneradorSqlCargue.ConsultarPrefijos: filas de PrefijoMunicipio y, al final,
    // filas solo-departamento de PrefijosDepartamento (con sus nombres, tildes incluidas).
    private static PrefijosFijo Prefijos() => PrefijosFijo.Cargar(
    [
        ("N. DE SANTANDER", "CUCUTA", "607"),
        ("ANTIOQUIA", "MEDELLIN", "604"),
        ("BOGOTA", "BOGOTA, D.C.", "601"),
        ("NORTE DE SANTANDER", "", "607"),
        ("ANTIOQUIA", "", "604"),
        ("BOGOTÁ D.C.", "", "601"),
    ]);

    private static ContextoCargue Ctx()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Camara);
        ctx.Prefijos = Prefijos();
        return ctx;
    }

    [Theory]
    [InlineData("2345678", "NORTE DE SANTANDER", null, "6072345678")]   // solo en PrefijosDepartamento
    [InlineData("2345678", "BOGOTÁ D.C.", "BOGOTÁ D.C.", "6012345678")]
    [InlineData("2345678", "bogota d.c.", null, "6012345678")]          // sin tilde (el SP no casaba)
    [InlineData("2345678.0", null, "Medellín", "6042345678")]           // por municipio; ".0" de Excel
    public void Fijo_de_7_recibe_el_indicativo(string tel, string? depto, string? municipio, string esperado)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("NumeroTelefono", tel), ("Departamento", depto), ("Municipio", municipio));

        _regla.Transformar(fila, ctx);

        Assert.Equal(esperado, ctx.Get(fila, "NumeroTelefono"));
        Assert.Empty(ctx.Advertencias());
    }

    [Fact]
    public void Fijo_de_7_sin_indicativo_resoluble_se_anula_con_aviso()
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("NumeroTelefono", "2345678"), ("Departamento", "HESSE"));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "NumeroTelefono"));
        Assert.Equal("HESSE", ctx.Get(fila, "Departamento"));   // exterior: se guarda
        Assert.Equal(1, Avisos(ctx, "NumeroTelefono: fijo de 7 dígitos sin prefijo resoluble"));
    }

    [Theory]
    [InlineData("3001234567")]
    [InlineData("6041234567")]
    [InlineData("12345678")]      // 8 dígitos: lo anula el motor, no la regla
    public void Otros_largos_pasan_sin_tocar(string tel)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("NumeroTelefono", tel), ("Departamento", "ANTIOQUIA"));

        _regla.Transformar(fila, ctx);

        Assert.Equal(tel, ctx.Get(fila, "NumeroTelefono"));
    }

    [Theory]
    [InlineData("Seleccione un departamento")]
    [InlineData("Cargando...")]
    public void Relleno_del_formulario_en_departamento_se_anula_y_no_da_indicativo(string depto)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("NumeroTelefono", "2345678"), ("Departamento", depto));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "Departamento"));
        Assert.Null(ctx.Get(fila, "NumeroTelefono"));
        Assert.Equal(1, Avisos(ctx, "Departamento: texto de relleno del formulario"));
    }

    [Fact]
    public void Municipio_sin_letras_se_anula()
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Municipio", "."), ("Departamento", "ANTIOQUIA"));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "Municipio"));
        Assert.Equal("ANTIOQUIA", ctx.Get(fila, "Departamento"));
        Assert.Equal(1, Avisos(ctx, "Municipio: valor sin letras anulado"));
    }

    [Fact]
    public void Validacion_generica_despues_de_la_regla_anula_lo_que_no_es_telefono()
    {
        // La regla deja pasar 10 dígitos que no empiezan en 3 ni en 6; el motor (tipo Telefono) los anula.
        var ctx = Ctx();
        var col = ctx.Definicion.ColumnasDato.Single(c => c.Nombre == "NumeroTelefono");

        Assert.Null(CargueFuentesMotor.ValidarContacto(col, "5551234567", ctx));
        Assert.Equal("6072345678", CargueFuentesMotor.ValidarContacto(col, "6072345678", ctx));
    }

    [Fact]
    public void Regla_pide_prefijos()
    {
        Assert.True(_regla.NecesitaPrefijos);
        Assert.IsType<ReglaCamara>(CatalogoReglasFuente.Obtener(CatalogoCargueFuentes.Camara.Fuente));
    }
}

using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Reglas propias de Cajacopi (traducción de sp_ProcesarCajacopiAuxiliar). Lo genérico
/// (fijo 6xx, celular 3xx, lista negra) no se prueba aquí: lo cubre el motor.
/// </summary>
public class ReglaCajacopiTests
{
    private static readonly ReglaCajacopi Regla = new();

    // Filas como están en dbo.PrefijoMunicipio (Bogotá con coma en el municipio).
    private static ContextoCargue Ctx() => new(CatalogoCargueFuentes.Cajacopi)
    {
        Prefijos = PrefijosFijo.Cargar(
        [
            ("BOGOTA", "BOGOTA, D.C.", "601"),
            ("ATLANTICO", "BARRANQUILLA", "605"),
            ("ATLANTICO", "SOLEDAD", "605"),
            ("SANTANDER", "BUCARAMANGA", "607"),
            ("SANTANDER", "OTRO", "608"),
        ]),
    };

    [Fact]
    public void Pide_prefijos_y_no_lista_negra_de_telefonos()
    {
        IReglaFuente r = Regla;
        Assert.True(r.NecesitaPrefijos);
        Assert.False(r.NecesitaListaNegraTelefonos);
    }

    [Theory]
    [InlineData("BOGOTA D.C.", "BOGOTA D.C.", "2345678", "6012345678")]      // sin tilde (caso del plan)
    [InlineData("Bogotá D. C.", "BOGOTÁ, D.C.", "2345678", "6012345678")]     // como viene en la BD real
    [InlineData("BOGOTA D.C.", null, "2345678", "6012345678")]               // distrito sin municipio
    [InlineData("atlántico", null, "3456789", "6053456789")]                 // departamento unívoco
    [InlineData(null, "Barranquilla", "3456789.0", "6053456789")]            // municipio solo, ".0" de Excel
    public void Fijo_de_7_digitos_recibe_el_indicativo(string? depto, string? municipio, string fijo, string esperado)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Departamento", depto), ("Municipio", municipio), ("NumeroTelefonoFijo", fijo));

        Regla.Transformar(fila, ctx);

        Assert.Equal(esperado, ctx.Get(fila, "NumeroTelefonoFijo"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData("SANTANDER", null)]        // departamento con dos indicativos: ambiguo
    [InlineData("NO EXISTE", "NI ESTE")]
    [InlineData(null, null)]
    public void Fijo_de_7_sin_prefijo_resoluble_se_anula_con_advertencia(string? depto, string? municipio)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Departamento", depto), ("Municipio", municipio), ("NumeroTelefonoFijo", "2345678"));

        Regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "NumeroTelefonoFijo"));
        Assert.Equal(1, Avisos(ctx, "sin prefijo resoluble"));
    }

    [Theory]
    [InlineData("1111111")]
    [InlineData("6051111111")]
    [InlineData("11111")]
    public void Fijo_de_relleno_11111_se_anula(string fijo)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Departamento", "ATLANTICO"), ("NumeroTelefonoFijo", fijo));

        Regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "NumeroTelefonoFijo"));
        Assert.Equal(1, Avisos(ctx, "'11111'"));
    }

    [Fact]
    public void El_11111_se_mira_antes_del_indicativo()
    {
        // "601" + "1111234" = "6011111234" contiene 11111, pero el número que mandó la fuente no.
        var ctx = Ctx();
        var fila = Fila(ctx, ("Departamento", "BOGOTA D.C."), ("NumeroTelefonoFijo", "1111234"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("6011111234", ctx.Get(fila, "NumeroTelefonoFijo"));
    }

    [Fact]
    public void Fijo_de_10_digitos_no_se_toca()
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Departamento", "ATLANTICO"), ("NumeroTelefonoFijo", "6053456789"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("6053456789", ctx.Get(fila, "NumeroTelefonoFijo"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData("NOTIENE@GMAIL.COM")]
    [InlineData("notiene")]
    [InlineData("NO TIENE")]
    [InlineData("no tiene correo")]
    public void Correo_NO_TIENE_se_anula(string email)
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Email", email));

        Regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "Email"));
        Assert.Equal(1, Avisos(ctx, "'NO TIENE'"));
    }

    [Fact]
    public void Correo_normal_y_celular_no_se_tocan()
    {
        var ctx = Ctx();
        var fila = Fila(ctx, ("Email", "juan.perez@gmail.com"), ("NumeroTelefonoCelular", "3001234567"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("juan.perez@gmail.com", ctx.Get(fila, "Email"));
        Assert.Equal("3001234567", ctx.Get(fila, "NumeroTelefonoCelular"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Fila_con_solo_basura_queda_sin_datos_de_contacto()
    {
        // El SP calculaba el flag ANTES de limpiar: esta fila quedaba con EstadoEnCajacopi = 1.
        // Tras la regla no queda ningún dato que entre al flag; el motor lo calcula después.
        var ctx = Ctx();
        var fila = Fila(ctx, ("NumeroTelefonoFijo", "1111111"), ("Email", "NO TIENE"), ("Municipio", "SOLEDAD"));

        Regla.Transformar(fila, ctx);

        var def = CatalogoCargueFuentes.Cajacopi;
        Assert.All(def.ColumnasDato.Where(c => c.EntraAlFlag), c => Assert.Null(ctx.Get(fila, c.Nombre)));
    }
}

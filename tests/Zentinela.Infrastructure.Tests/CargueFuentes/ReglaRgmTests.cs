using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// RGM (traducción de sp_ProcesarRGMAuxiliar): la regla solo valida el año del Modelo; la placa la
/// valida el motor como toda columna tipo Placa (PlacaVehiculo, la de SIMIT), con valores reales de dbo.RGM.
/// </summary>
public class ReglaRgmTests
{
    private static readonly ReglaRgm Regla = new();

    private static ColumnaFuenteCargue Columna(string nombre) =>
        CatalogoCargueFuentes.Rgm.ColumnasDato.Single(c => c.Nombre == nombre);

    [Theory]
    [InlineData(1991L)]    // el más viejo en dbo.RGM
    [InlineData(2026L)]
    [InlineData(1900L)]
    public void Modelo_plausible_se_conserva(long modelo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var fila = Fila(ctx, ("Marca", "CHEVROLET"), ("Modelo", modelo));

        Regla.Transformar(fila, ctx);

        Assert.Equal(modelo, ctx.Get(fila, "Modelo"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Modelo_del_anio_siguiente_se_conserva_y_el_limite_es_el_actual_mas_dos()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var siguiente = Fila(ctx, ("Modelo", (long)(DateTime.Today.Year + 1)));
        var limite = Fila(ctx, ("Modelo", (long)ReglaRgm.ModeloMaximo));

        Regla.Transformar(siguiente, ctx);
        Regla.Transformar(limite, ctx);

        Assert.Equal(DateTime.Today.Year + 2, ReglaRgm.ModeloMaximo);
        Assert.NotNull(ctx.Get(siguiente, "Modelo"));
        Assert.NotNull(ctx.Get(limite, "Modelo"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData(2323L)]              // hay uno así en dbo.RGM
    [InlineData(1899L)]
    [InlineData(21L)]
    [InlineData(20252025252L)]       // no cabe en int: sin la regla, desbordaría el INSERT de todo el cargue
    public void Modelo_implausible_se_anula_con_aviso_y_la_fila_sigue(long modelo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var fila = Fila(ctx, ("Marca", "MAZDA"), ("Modelo", modelo), ("Placa", "ABC123"));

        Regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "Modelo"));
        Assert.Equal("MAZDA", ctx.Get(fila, "Marca"));
        Assert.Equal("ABC123", ctx.Get(fila, "Placa"));
        Assert.Equal(1, Avisos(ctx, ReglaRgm.AvisoModelo));
        Assert.Null(fila.MotivoExclusion);
    }

    [Fact]
    public void Sin_modelo_no_hace_nada()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var fila = Fila(ctx, ("Marca", "KIA"));

        Regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "Modelo"));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    // ── Placa: validación genérica del motor con valores reales de dbo.RGM ──────

    [Theory]
    [InlineData("KNR827", "KNR827")]    // AAA999 (carro)
    [InlineData("LZB10H", "LZB10H")]    // AAA99A (moto)
    [InlineData(" HGK96", "HGK96")]     // espacio inicial, como en dbo.RGM
    [InlineData("kya-12", "KYA12")]
    [InlineData("SSW14", "SSW14")]      // AAA99 (moto vieja)
    public void Placa_valida_se_normaliza(string entrada, string esperada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);

        Assert.Equal(esperada, CargueFuentesMotor.ValidarContacto(Columna("Placa"), entrada, ctx));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Theory]
    [InlineData("[objec")]              // '[object Object]' truncado a 6: 21 filas en dbo.RGM
    [InlineData("[object Object]")]
    [InlineData("AVAL")]                // 3 filas en dbo.RGM
    public void Lo_que_no_es_placa_se_anula_con_aviso(string entrada)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);

        Assert.Null(CargueFuentesMotor.ValidarContacto(Columna("Placa"), entrada, ctx));
        Assert.Equal(1, Avisos(ctx, "Placa: valor que no es una placa anulado"));
    }

    [Fact]
    public void No_pide_prefijos_ni_lista_negra_de_telefonos_ni_expande()
    {
        IReglaFuente r = Regla;
        var ctx = Contexto(CatalogoCargueFuentes.Rgm);
        var fila = Fila(ctx, ("Marca", "KIA"));

        Assert.False(r.NecesitaPrefijos);
        Assert.False(r.NecesitaListaNegraTelefonos);
        Assert.Same(fila, Assert.Single(r.Expandir(fila, ctx)));
    }
}

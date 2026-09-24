using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Reubicación de teléfonos de MutualSer (traducción de sp_ProcesarMutualSerAuxiliar).
/// Lo que la regla deja mal ubicado lo anula después la validación genérica del motor.
/// </summary>
public class ReglaMutualSerTests
{
    private const string Cel = "NumeroTelefonoCelular", Cel2 = "NumeroTelefonoCelular2", Fijo = "NumeroTelefonoFijo";
    private static readonly ReglaMutualSer Regla = new();

    private static (ContextoCargue Ctx, FilaCargue Fila) Transformar(string? cel, string? cel2, string? fijo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.MutualSer);
        var fila = Fila(ctx, (Cel, cel), (Cel2, cel2), (Fijo, fijo));
        Regla.Transformar(fila, ctx);
        return (ctx, fila);
    }

    // "celular|celular2|fijo" (NULL = vacío): comparable de una vez en cada caso.
    private static string Tels(string? cel, string? cel2, string? fijo) => string.Join("|", cel, cel2, fijo);

    private static string Telefonos(ContextoCargue ctx, FilaCargue f) =>
        Tels(ctx.GetTexto(f, Cel), ctx.GetTexto(f, Cel2), ctx.GetTexto(f, Fijo));

    [Fact]
    public void Celular_60x_pasa_al_fijo_vacio()
    {
        var (ctx, f) = Transformar("6012345678", null, null);
        Assert.Equal(Tels(null, null, "6012345678"), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Celular_60x_con_fijo_ocupado_se_descarta_sin_concatenar()
    {
        // El SP hacía fijo + ', ' + celular en un varchar(10): truncaba el fijo.
        var (ctx, f) = Transformar("6012345678", null, "6049876543");
        Assert.Equal(Tels(null, null, "6049876543"), Telefonos(ctx, f));
        Assert.Equal(1, Avisos(ctx, "fijo en la columna de celular descartado"));
    }

    [Fact]
    public void Dos_celulares_60x_el_segundo_se_descarta()
    {
        var (ctx, f) = Transformar("6012345678", "6023456789", null);
        Assert.Equal(Tels(null, null, "6012345678"), Telefonos(ctx, f));
        Assert.Equal(1, Avisos(ctx, $"{Cel2}: fijo en la columna de celular descartado"));
    }

    [Fact]
    public void Celular_60x_igual_al_fijo_no_es_conflicto()
    {
        var (ctx, f) = Transformar("6012345678", null, "601 234 5678");
        Assert.Equal(Tels(null, null, "6012345678"), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Celular_60x_reemplaza_un_fijo_invalido_con_advertencia()
    {
        var (ctx, f) = Transformar("6012345678", null, "12345");
        Assert.Equal(Tels(null, null, "6012345678"), Telefonos(ctx, f));
        Assert.Equal(1, Avisos(ctx, "valor inválido reemplazado"));
    }

    [Theory]
    [InlineData("3001234567", "3001234567")]
    [InlineData("3001234567", "57 300 123 4567")]   // mismo número tras normalizar
    public void Celular2_igual_al_celular_se_anula(string cel, string cel2)
    {
        var (ctx, f) = Transformar(cel, cel2, null);
        Assert.Equal(Tels("3001234567", null, null), Telefonos(ctx, f));
    }

    [Fact]
    public void Fijo_que_es_celular_pasa_al_celular2_libre()
    {
        var (ctx, f) = Transformar("3001234567", null, "3009998877");
        Assert.Equal(Tels("3001234567", "3009998877", null), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Fijo_que_es_celular_con_celular2_ocupado_queda_para_la_validacion_generica()
    {
        var (ctx, f) = Transformar("3001234567", "3007654321", "3009998877");
        Assert.Equal(Tels("3001234567", "3007654321", "3009998877"), Telefonos(ctx, f));
    }

    [Theory]
    [InlineData("3001234567", null, "3001234567")]           // fijo repite el celular
    [InlineData("3001234567", "3007654321", "3007654321")]   // fijo repite el celular 2
    public void Fijo_repetido_en_un_celular_se_anula(string cel, string? cel2, string fijo)
    {
        var (ctx, f) = Transformar(cel, cel2, fijo);
        Assert.Null(ctx.GetTexto(f, Fijo));
        Assert.Equal(cel, ctx.GetTexto(f, Cel));
        Assert.Equal(cel2, ctx.GetTexto(f, Cel2));
    }

    [Fact]
    public void Intercambio_completo_no_pierde_ningun_numero()
    {
        // Con el orden del SP (60x primero) el fijo "ocupado" por un celular hacía perder el 60x.
        var (ctx, f) = Transformar("6012345678", null, "3009998877");
        Assert.Equal(Tels(null, "3009998877", "6012345678"), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Numeros_bien_ubicados_no_se_tocan()
    {
        var (ctx, f) = Transformar("3001234567", "3007654321", "6041234567");
        Assert.Equal(Tels("3001234567", "3007654321", "6041234567"), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Basura_se_deja_tal_cual_para_que_la_anule_el_motor()
    {
        var (ctx, f) = Transformar("123", null, "abc");
        Assert.Equal(Tels("123", null, "abc"), Telefonos(ctx, f));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }
}

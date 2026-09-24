using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>Despivote de Placa1..Placa15 (traducción de sp_ProcesarSIMITAuxiliar, paso 6).</summary>
public class ReglaSimitTests
{
    private static readonly ReglaSimit Regla = new();

    private static (ContextoCargue Ctx, List<FilaCargue> Filas) Expandir(params string?[] placas)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Simit);
        var extra = new string?[15];
        Array.Copy(placas, extra, placas.Length);
        var fila = FilaConExtra(ctx, extra);
        fila.CarteraID = 7;
        fila.LiderID = 3;
        fila.Nombre = "JUAN";
        return (ctx, Regla.Expandir(fila, ctx).ToList());
    }

    private static string Placas(ContextoCargue ctx, List<FilaCargue> filas) =>
        string.Join(",", filas.Select(f => ctx.GetTexto(f, "Placa") ?? "NULL"));

    [Fact]
    public void Tres_placas_son_tres_filas()
    {
        var (ctx, filas) = Expandir("ABC123", "DEF456", "GHI789");
        Assert.Equal("ABC123,DEF456,GHI789", Placas(ctx, filas));
        Assert.All(filas, f => Assert.Equal(1_234_567, f.TitularID));
        Assert.All(filas, f => Assert.Equal(("JUAN", 7, 3), (f.Nombre, f.CarteraID, f.LiderID)));
        Assert.Equal(3, filas.Distinct().Count());   // filas distintas, no la misma referencia
    }

    [Fact]
    public void Sin_placas_es_una_fila_con_placa_NULL()
    {
        var (ctx, filas) = Expandir(null, "", "  ", "0", "NULL");
        Assert.Equal("NULL", Placas(ctx, filas));
    }

    [Fact]
    public void Placas_repetidas_o_con_otra_forma_se_unen()
    {
        // El DISTINCT del SP las trataba como distintas; Placa15 (nchar) traía espacios de relleno.
        var (ctx, filas) = Expandir("ABC123", "abc 123", null, "DEF456", "ABC123   ");
        Assert.Equal("ABC123,DEF456", Placas(ctx, filas));
    }

    [Fact]
    public void Placa_de_mas_de_10_se_descarta_con_advertencia()
    {
        var (ctx, filas) = Expandir("ABCDEFGHIJK", "XYZ987");
        Assert.Equal("XYZ987", Placas(ctx, filas));
        Assert.Equal(1, Avisos(ctx, "placa de más de 10 caracteres descartada"));
    }

    [Fact]
    public void Solo_placas_largas_deja_la_fila_sin_placa()
    {
        var (ctx, filas) = Expandir("ABCDEFGHIJK");
        Assert.Equal("NULL", Placas(ctx, filas));
        Assert.Equal(1, Avisos(ctx, "descartada"));
    }

    [Fact]
    public void Lee_hasta_la_placa_15()
    {
        var placas = new string?[15];
        placas[14] = "OPQ321";
        var (ctx, filas) = Expandir(placas);
        Assert.Equal("OPQ321", Placas(ctx, filas));
    }
}

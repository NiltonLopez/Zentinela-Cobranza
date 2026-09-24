using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Regla propia de Fosyga (traducción de sp_ProcesarFosygaAuxiliar, paso 4): Estado
/// 'AFILIADO FALLECIDO' → TipoAfiliado 'FALLECIDO'. El flag (Estado/EPS/Régimen) es de la definición.
/// </summary>
public class ReglaFosygaTests
{
    private static readonly ReglaFosyga Regla = new();

    private static string Info(ContextoCargue ctx) =>
        string.Join("|", ctx.Info().Select(i => $"{i.Mensaje}x{i.Cantidad}"));

    [Theory]
    [InlineData("AFILIADO FALLECIDO", "COTIZANTE")]
    [InlineData("afiliado fallecido", "BENEFICIARIO")]   // la collation CI del SP tampoco distinguía
    [InlineData("AFILIADO FALLECIDO", null)]
    public void Afiliado_fallecido_pone_TipoAfiliado_FALLECIDO(string estado, string? tipo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Fosyga);
        var fila = Fila(ctx, ("Estado", estado), ("TipoAfiliado", tipo), ("EPS", "NUEVA EPS S.A."));

        Regla.Transformar(fila, ctx);

        Assert.Equal("FALLECIDO", ctx.Get(fila, "TipoAfiliado"));
        Assert.Equal(estado, ctx.Get(fila, "Estado"));                // el Estado no se toca
        Assert.Equal("NUEVA EPS S.A.", ctx.Get(fila, "EPS"));
        Assert.Equal($"{ReglaFosyga.InfoFallecido}x1", Info(ctx));
        Assert.Equal(0, ctx.TotalAdvertencias);
    }

    [Fact]
    public void Ya_FALLECIDO_no_suma_info()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Fosyga);
        var fila = Fila(ctx, ("Estado", "AFILIADO FALLECIDO"), ("TipoAfiliado", "FALLECIDO"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("FALLECIDO", ctx.Get(fila, "TipoAfiliado"));
        Assert.Empty(ctx.Info());
    }

    [Theory]
    [InlineData("ACTIVO")]
    [InlineData("RETIRADO")]
    [InlineData("FALLECIDO")]               // el SP compara con 'AFILIADO FALLECIDO' exacto
    [InlineData("AFILIADO FALLECIDO X")]
    [InlineData(null)]
    public void Otro_estado_no_toca_TipoAfiliado(string? estado)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Fosyga);
        var fila = Fila(ctx, ("Estado", estado), ("TipoAfiliado", "COTIZANTE"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("COTIZANTE", ctx.Get(fila, "TipoAfiliado"));
        Assert.Empty(ctx.Info());
    }

    [Theory]
    [InlineData("AFILIADO\nFALLECIDO")]      // 69 filas así en dbo.Fosyga: el '=' del SP no las casaba
    [InlineData("AFILIADO\r\nFALLECIDO")]
    [InlineData("  AFILIADO   FALLECIDO ")]
    public void Salto_de_linea_en_la_celda_se_colapsa_antes_de_la_regla(string bruto)
    {
        // El tipado genérico (LimpiarTexto) corre ANTES de Transformar.
        var ctx = Contexto(CatalogoCargueFuentes.Fosyga);
        var fila = Fila(ctx, ("Estado", CargueFuentesMotor.LimpiarTexto(bruto)), ("TipoAfiliado", "COTIZANTE"));

        Regla.Transformar(fila, ctx);

        Assert.Equal("AFILIADO FALLECIDO", ctx.Get(fila, "Estado"));
        Assert.Equal("FALLECIDO", ctx.Get(fila, "TipoAfiliado"));
    }

    [Fact]
    public void No_pide_prefijos_ni_lista_negra_de_telefonos_ni_expande()
    {
        IReglaFuente r = Regla;
        var ctx = Contexto(CatalogoCargueFuentes.Fosyga);
        var fila = Fila(ctx, ("Estado", "ACTIVO"));

        Assert.False(r.NecesitaPrefijos);
        Assert.False(r.NecesitaListaNegraTelefonos);
        Assert.Same(fila, Assert.Single(r.Expandir(fila, ctx)));
    }
}

using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Traducción de sp_ProcesarCompensadosAuxiliar: marca 'NO SE ENCUENTRA' anulada; el flag NO
/// viene del archivo (la columna Estado no está en la definición).
/// </summary>
public class ReglaCompensadosTests
{
    private readonly ReglaCompensados _regla = new();

    [Theory]
    [InlineData("NO SE ENCUENTRA")]
    [InlineData("No se encuentra")]
    [InlineData("NO ENCONTRADO")]
    [InlineData("SIN INFORMACIÓN")]
    public void Marca_de_no_encontrado_en_TipoAfiliado_se_anula(string marca)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("TipoAfiliado", marca));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "TipoAfiliado"));
        Assert.Equal(1, Avisos(ctx, "TipoAfiliado: marca 'NO SE ENCUENTRA'"));
    }

    [Fact]
    public void Marca_en_EPS_tambien_se_anula()
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("EPS", "NO SE ENCUENTRA"), ("TipoAfiliado", "COTIZANTE"));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "EPS"));
        Assert.Equal("COTIZANTE", ctx.Get(fila, "TipoAfiliado"));
    }

    [Theory]
    [InlineData("Nueva Eps", "COTIZANTE")]
    [InlineData("Salud Total", "BENEFICIARIO")]
    [InlineData("Sanitas", "ADICIONAL")]
    public void Datos_reales_pasan_sin_tocar(string eps, string tipo)
    {
        var ctx = Contexto(CatalogoCargueFuentes.Compensados);
        var fila = Fila(ctx, ("EPS", eps), ("TipoAfiliado", tipo));

        _regla.Transformar(fila, ctx);

        Assert.Equal(eps, ctx.Get(fila, "EPS"));
        Assert.Equal(tipo, ctx.Get(fila, "TipoAfiliado"));
        Assert.Empty(ctx.Advertencias());
    }

    [Fact]
    public void El_flag_sale_de_EPS_y_periodo_y_la_columna_Estado_del_archivo_no_existe()
    {
        var d = CatalogoCargueFuentes.Compensados;
        Assert.Equal(["EPS", "UltimoPeriodoCompensado"], d.ColumnasDato.Where(c => c.EntraAlFlag).Select(c => c.Nombre));
        Assert.DoesNotContain(d.ColumnasDato.Concat(d.ColumnasArchivoExtra), c => c.Alias.Contains("estado"));
        Assert.IsType<ReglaCompensados>(CatalogoReglasFuente.Obtener(d.Fuente));
    }
}

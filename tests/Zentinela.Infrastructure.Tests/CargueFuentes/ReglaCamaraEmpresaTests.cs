using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using Zentinela.Infrastructure.SQLServer.Services.Shared;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Traducción de sp_ProcesarCamaraEmpresaAuxiliar: clave NIT sin dígito de verificación e
/// indicativo del fijo de 7 dígitos por el Departamento (columna extra, no se guarda).
/// </summary>
public class ReglaCamaraEmpresaTests
{
    private readonly ReglaCamaraEmpresa _regla = new();

    private static ContextoCargue Ctx()
    {
        var ctx = Contexto(CatalogoCargueFuentes.CamaraEmpresa);
        ctx.Prefijos = PrefijosFijo.Cargar([("ANTIOQUIA", "MEDELLIN", "604"), ("NORTE DE SANTANDER", "", "607")]);
        return ctx;
    }

    [Theory]
    [InlineData("900123456-7", "900123456")]
    [InlineData("900.123.456-7", "900.123.456")]   // los puntos los quita el motor al validar
    [InlineData(" 900123456 - 7 ", "900123456")]
    [InlineData("900123456", "900123456")]         // sin DV: tal cual
    [InlineData("900-123", "900-123")]             // no es 'NIT-DV' (el DV es un solo dígito): tal cual
    [InlineData(null, null)]
    public void Quita_el_digito_de_verificacion(string? bruto, string? esperado)
    {
        var ctx = Ctx();

        Assert.Equal(esperado, _regla.NormalizarClave(bruto, ctx));
    }

    [Fact]
    public void Informa_cuantos_NIT_traian_DV()
    {
        var ctx = Ctx();
        _regla.NormalizarClave("900123456-7", ctx);
        _regla.NormalizarClave("800111222-3", ctx);
        _regla.NormalizarClave("800111222", ctx);

        Assert.Equal(2, ctx.Info().Single(i => i.Mensaje.Contains("dígito de verificación")).Cantidad);
    }

    [Theory]
    [InlineData("2345678", "NORTE DE SANTANDER", "", "6072345678")]
    [InlineData("2345678", "", "Medellín", "6042345678")]
    [InlineData("3001234567", "ANTIOQUIA", "", "3001234567")]
    public void Fijo_de_7_toma_el_indicativo_del_departamento_del_archivo(string tel, string depto, string municipio, string esperado)
    {
        var ctx = Ctx();
        var fila = FilaConExtra(ctx, [depto, municipio], ("NumeroTelefono", tel));

        _regla.Transformar(fila, ctx);

        Assert.Equal(esperado, ctx.Get(fila, "NumeroTelefono"));
    }

    [Theory]
    [InlineData("Seleccione un departamento")]
    [InlineData("  ")]
    [InlineData("NO EXISTE")]
    public void Sin_indicativo_resoluble_el_fijo_se_anula_con_aviso(string depto)
    {
        var ctx = Ctx();
        var fila = FilaConExtra(ctx, [depto, null], ("NumeroTelefono", "2345678"));

        _regla.Transformar(fila, ctx);

        Assert.Null(ctx.Get(fila, "NumeroTelefono"));
        Assert.Equal(1, Avisos(ctx, "NumeroTelefono: fijo de 7 dígitos sin prefijo resoluble"));
    }

    [Fact]
    public void Definicion_sin_Titular_y_con_regla_registrada()
    {
        var d = CatalogoCargueFuentes.CamaraEmpresa;
        Assert.False(d.CreaTitular || d.CreaTitularCartera);
        Assert.Equal("NitEmpresa", d.ColumnaClaveTitular);
        Assert.IsType<ReglaCamaraEmpresa>(CatalogoReglasFuente.Obtener(d.Fuente));
        Assert.True(_regla.NecesitaPrefijos);
    }
}

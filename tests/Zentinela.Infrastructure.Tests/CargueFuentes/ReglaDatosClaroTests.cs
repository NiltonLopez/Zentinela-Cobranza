using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;
using static Zentinela.Infrastructure.Tests.CargueFuentes.ReglaTestUtil;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Datos Claro (traducción de sp_ProcesarDatosClaroAuxiliar). El cruce con ListaNegraTelefonos
/// es del motor (BD real) y lo cubre el harness; aquí, la exclusión propia de la fuente.
/// </summary>
public class ReglaDatosClaroTests
{
    private static readonly ReglaDatosClaro Regla = new();

    [Fact]
    public void Pide_lista_negra_de_telefonos_y_no_prefijos()
    {
        IReglaFuente r = Regla;
        Assert.True(r.NecesitaListaNegraTelefonos);
        Assert.False(r.NecesitaPrefijos);
    }

    [Fact]
    public void Sin_telefono_ni_email_se_excluye()
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);
        var fila = Fila(ctx, ("NumeroTelefono", null), ("Email", null));

        Regla.Validar(fila, ctx);

        Assert.Equal("Sin teléfono ni email válidos tras la limpieza", fila.MotivoExclusion);
        Assert.Equal(1, ctx.Errores().Single().Cantidad);
    }

    [Theory]
    [InlineData("3001234567", null)]
    [InlineData(null, "juan@claro.com.co")]
    [InlineData("6041234567", "juan@claro.com.co")]
    public void Con_telefono_o_email_entra(string? telefono, string? email)
    {
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);
        var fila = Fila(ctx, ("NumeroTelefono", telefono), ("Email", email));

        Regla.Validar(fila, ctx);

        Assert.Null(fila.MotivoExclusion);
        Assert.Empty(ctx.Errores());
    }

    [Fact]
    public void Revalidar_una_fila_ya_excluida_no_la_cuenta_dos_veces()
    {
        // El motor vuelve a llamar Validar tras anular un teléfono en lista negra.
        var ctx = Contexto(CatalogoCargueFuentes.DatosClaro);
        var fila = Fila(ctx);

        Regla.Validar(fila, ctx);
        Regla.Validar(fila, ctx);

        Assert.Equal(1, ctx.Errores().Sum(e => e.Cantidad));
    }
}

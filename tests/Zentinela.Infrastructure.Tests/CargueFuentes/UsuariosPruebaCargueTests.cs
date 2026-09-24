using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// La lista "Cargues en curso y recientes" oculta las ejecuciones del harness de pruebas
/// (tools/CargueHarness), que escribe en la BD real mientras corre. Se reconocen por el prefijo
/// de su cuenta; una cuenta real nunca debe caer en el filtro.
/// </summary>
public class UsuariosPruebaCargueTests
{
    [Theory]
    [InlineData("harness-cargue")]
    [InlineData("harness-cargue-salud")]
    [InlineData("harness-cargue-empresas")]
    [InlineData("harness-cargue-otras")]
    [InlineData("harness-cargue-desglose")]
    [InlineData("Harness-Cargue-Empresas")]
    public void Cuentas_del_harness_son_de_prueba(string usuario) =>
        Assert.True(UsuariosPruebaCargue.EsDePrueba(usuario));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nlopez")]
    [InlineData("harness")]
    [InlineData("auxiliar.harness-cargue")]
    [InlineData("desconocido")]
    public void Cuentas_reales_no_son_de_prueba(string? usuario) =>
        Assert.False(UsuariosPruebaCargue.EsDePrueba(usuario));

    [Fact]
    public void El_prefijo_no_trae_comodines_de_LIKE() =>
        // ListarAsync lo usa como patrón LIKE sin escapar.
        Assert.DoesNotMatch(@"[%_\[]", UsuariosPruebaCargue.Prefijo);
}

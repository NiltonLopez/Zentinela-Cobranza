using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class TelefonoColombianoTests
{
    [Theory]
    [InlineData("+57 300 123 4567", "3001234567")]
    [InlineData("3001234567", "3001234567")]
    [InlineData("6041234567", null)]      // fijo en columna de celular
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public void NormalizarCelular_casos(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, TelefonoColombiano.NormalizarCelular(entrada));
    }

    [Theory]
    [InlineData("6041234567", "6041234567")]
    [InlineData("604 123 4567", "6041234567")]
    [InlineData("+57 300 123 4567", null)] // celular en columna de fijo
    [InlineData("3001234567", null)]
    [InlineData("1234567890", null)]       // 10 dígitos pero no empieza en 6
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public void NormalizarFijo_casos(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, TelefonoColombiano.NormalizarFijo(entrada));
    }
}

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

    // S3 (PARIDAD-salud.md): la parte decimal nula de un CSV exportado por Excel o pandas.
    [Theory]
    [InlineData("3001234567.0", "3001234567")]         // pandas
    [InlineData("3001234567.00", "3001234567")]
    [InlineData("3001234567,0", "3001234567")]         // Excel es-CO
    [InlineData("3001234567,00", "3001234567")]
    [InlineData("3.001.234.567,00", "3001234567")]     // Excel es-CO con miles
    [InlineData("573001234567.0", "3001234567")]       // indicativo + decimal
    [InlineData("300.123.45.67", "3001234567")]        // formato con puntos: no es decimal
    [InlineData("300123456.0", null)]                  // 9 dígitos: el .0 no cuenta como décimo
    [InlineData("300123456.7", null)]                  // decimal real: no es un teléfono
    [InlineData("3001234567.5", null)]
    [InlineData("3001234567.0.0", null)]               // UNA sola parte decimal
    [InlineData("5,73001E+11", null)]                  // Excel en General con 12 dígitos: con pérdida
    public void NormalizarCelular_con_parte_decimal(string entrada, string? esperado)
    {
        Assert.Equal(esperado, TelefonoColombiano.NormalizarCelular(entrada));
    }

    [Fact]
    public void NormalizarFijo_con_parte_decimal_de_excel()
    {
        Assert.Equal("6041234567", TelefonoColombiano.NormalizarFijo("6041234567,00"));
    }
}

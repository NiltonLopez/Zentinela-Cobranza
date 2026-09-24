using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

/// <summary>
/// Parte decimal que Excel (es-CO) y pandas le pegan a un entero al exportar a CSV (hallazgo S3
/// de docs/sql/legacy/PARIDAD-salud.md). Las formas de Excel se verificaron exportando a CSV.
/// </summary>
public class NumeroEnTextoTests
{
    [Theory]
    [InlineData("3001234567.0", "3001234567")]         // pandas (columna float)
    [InlineData("3001234567.00", "3001234567")]
    [InlineData("3001234567,0", "3001234567")]         // Excel es-CO, formato 0,0
    [InlineData("3001234567,00", "3001234567")]        // Excel es-CO, formato 0,00
    [InlineData("3.001.234.567,00", "3.001.234.567")]  // Excel es-CO, formato #.##0,00
    [InlineData("1,234,567.00", "1,234,567")]          // en-US con miles
    [InlineData(" 2021.0 ", "2021")]
    [InlineData("3001234567", "3001234567")]           // sin decimales: igual
    [InlineData("3.001.234.567", "3.001.234.567")]     // solo miles: igual
    [InlineData("300.123.45.00", "300.123.45.00")]     // el '.' ya apareció: formato, no decimal
    [InlineData("3001234567.0.0", "3001234567.0.0")]   // UNA sola parte decimal
    [InlineData("3001234567.000", "3001234567.000")]   // tres dígitos: miles, no decimal
    [InlineData("3001234567.5", "3001234567.5")]       // decimal real: no se toca
    public void QuitarDecimalNulo_casos(string entrada, string esperado)
    {
        Assert.Equal(esperado, NumeroEnTexto.QuitarDecimalNulo(entrada));
    }

    [Theory]
    [InlineData("300123456.7", true)]
    [InlineData("12345,25", true)]
    [InlineData("3.001.234.567,50", true)]
    [InlineData("2021.0", true)]         // sin pasar antes por QuitarDecimalNulo, un .0 es decimal
    [InlineData("3001234567", false)]
    [InlineData("1.234", false)]         // miles
    [InlineData("900.123.456", false)]
    [InlineData("300.123.45.67", false)] // formato de teléfono con puntos
    [InlineData("5,73001E+11", false)]   // notación científica: la rechaza quien exige dígitos
    public void TieneDecimales_casos(string entrada, bool esperado)
    {
        Assert.Equal(esperado, NumeroEnTexto.TieneDecimales(entrada));
    }
}

using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class NitColombianoTests
{
    [Theory]
    [InlineData("900123456-7", "900123456", true)]
    [InlineData("900.123.456-7", "900.123.456", true)]
    [InlineData(" 900123456 - 7 ", "900123456", true)]
    [InlineData("900123456", "900123456", false)]      // sin guion no se sabe si hay DV
    [InlineData("9001234567", "9001234567", false)]
    [InlineData("900123456-78", "900123456-78", false)] // el DV es UN dígito
    [InlineData("ABC-1", "ABC-1", false)]
    [InlineData(null, null, false)]
    public void QuitarDigitoVerificacion_casos(string? entrada, string? esperado, bool quitado)
    {
        Assert.Equal(esperado, NitColombiano.QuitarDigitoVerificacion(entrada, out var q));
        Assert.Equal(quitado, q);
    }
}

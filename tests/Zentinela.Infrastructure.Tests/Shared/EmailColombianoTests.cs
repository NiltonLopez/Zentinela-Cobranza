using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class EmailColombianoTests
{
    [Theory]
    [InlineData(" Juan.Perez@Gmail.COM ", "juan.perez@gmail.com")]
    [InlineData("juan perez@gmail.com", "juanperez@gmail.com")]   // espacio interno = error de digitación
    [InlineData("sin arroba", null)]
    [InlineData("a@b", null)]            // sin punto en el dominio
    [InlineData("@x.com", null)]         // empieza en '@'
    [InlineData("a@@b.com", null)]       // dos '@'
    [InlineData(".a@b.com", null)]       // empieza en '.'
    [InlineData("a@b.com.", null)]       // termina en '.'
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Normalizar_casos(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, EmailColombiano.Normalizar(entrada, 100));
    }

    [Fact]
    public void Normalizar_excede_largo_devuelve_null()
    {
        Assert.Null(EmailColombiano.Normalizar("ab@cde.com", 5));   // 10 caracteres
    }

    [Fact]
    public void Normalizar_largo_exacto_es_valido()
    {
        Assert.Equal("ab@cde.com", EmailColombiano.Normalizar("ab@cde.com", 10));
    }

    [Theory]
    [InlineData("juan@gmail.com", "gmail.com")]
    [InlineData("x@uniquindio.edu.co", "uniquindio.edu.co")]
    [InlineData("sinarroba", "")]
    public void Dominio_casos(string email, string esperado)
    {
        Assert.Equal(esperado, EmailColombiano.Dominio(email));
    }
}

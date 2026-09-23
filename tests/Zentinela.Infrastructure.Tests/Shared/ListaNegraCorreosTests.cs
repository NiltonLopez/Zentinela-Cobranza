using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class ListaNegraCorreosTests
{
    // Muestra de la tabla real ListaNegraCorreos.CorreoErroneo.
    private static readonly string[] MuestraReal =
    [
        "a@gmail.com", "NT@GMAIL.COM", "123@HOTMAIL.COM", "UNIQUINDIO.EDU.CO", "NOTEINE", "SIN_CORREO@GMAIL.COM",
    ];

    [Fact]
    public void Clasifica_la_muestra_real()
    {
        var lista = ListaNegraCorreos.Cargar(MuestraReal);

        Assert.Equal(4, lista.Emails);
        Assert.Equal(1, lista.Dominios);
        Assert.Equal(1, lista.Ignoradas);
    }

    [Fact]
    public void Igualdad_no_subcadena()
    {
        // Con LIKE '%a@gmail.com%' este correo válido quedaba anulado.
        var lista = ListaNegraCorreos.Cargar(MuestraReal);

        Assert.False(lista.Contiene("lauramejiacarepa@gmail.com"));
        Assert.True(lista.Contiene("a@gmail.com"));
    }

    [Fact]
    public void Entrada_en_mayusculas_bloquea_el_correo_normalizado()
    {
        var lista = ListaNegraCorreos.Cargar(MuestraReal);

        Assert.True(lista.Contiene("nt@gmail.com"));
        Assert.True(lista.Contiene("sin_correo@gmail.com"));
    }

    [Fact]
    public void Dominio_bloquea_todos_sus_correos()
    {
        var lista = ListaNegraCorreos.Cargar(["uniquindio.edu.co"]);

        Assert.True(lista.Contiene("x@uniquindio.edu.co"));
        Assert.False(lista.Contiene("x@gmail.com"));
    }

    [Fact]
    public void Entrada_sin_forma_de_correo_ni_dominio_se_ignora()
    {
        var lista = ListaNegraCorreos.Cargar(["NOTEINE"]);

        Assert.Equal(1, lista.Ignoradas);
        Assert.Equal(0, lista.Emails);
        Assert.Equal(0, lista.Dominios);
        Assert.False(lista.Contiene("noteine@gmail.com"));
    }

    [Fact]
    public void Correo_que_contiene_una_entrada_no_cae()
    {
        var lista = ListaNegraCorreos.Cargar(["notiene@x.com"]);

        Assert.False(lista.Contiene("juan.notiene@x.com"));
        Assert.True(lista.Contiene("notiene@x.com"));
    }

    [Fact]
    public void Entradas_vacias_se_saltan_sin_contarse()
    {
        var lista = ListaNegraCorreos.Cargar(["", "   ", "a@b.com"]);

        Assert.Equal(1, lista.Emails);
        Assert.Equal(0, lista.Ignoradas);
    }
}

using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

public class PrefijosFijoTests
{
    private static readonly PrefijosFijo Tabla = PrefijosFijo.Cargar(
    [
        ("ANTIOQUIA", "MEDELLIN", "604"),
        ("ANTIOQUIA", "BELLO", "604"),
        ("BOGOTÁ D.C.", "BOGOTA", "601"),
        ("CUNDINAMARCA", "SOACHA", "601"),
        ("VALLE DEL CAUCA", "CALI", "602"),
        ("X", "SOACHA", "607"),
    ]);

    [Theory]
    [InlineData("bogota d.c.", null, "601")]         // sin tilde y en minúsculas
    [InlineData("Bogota DC", null, "601")]           // sin puntos
    [InlineData("ANTIOQUIA", "medellín", "604")]     // par exacto, municipio con tilde
    [InlineData("ANTIOQUIA", null, "604")]           // departamento unívoco
    [InlineData(null, "SOACHA", null)]               // municipio repetido con prefijos distintos
    [InlineData("CUNDINAMARCA", "SOACHA", "601")]    // el par desambigua
    [InlineData(null, "CALI", "602")]                // municipio único
    [InlineData(null, null, null)]
    [InlineData("", "  ", null)]
    [InlineData("NO EXISTE", "NI ESTE", null)]
    public void Resolver_casos(string? departamento, string? municipio, string? esperado)
    {
        Assert.Equal(esperado, Tabla.Resolver(departamento, municipio));
    }

    [Fact]
    public void Departamento_con_prefijos_distintos_no_es_univoco()
    {
        var tabla = PrefijosFijo.Cargar(
        [
            ("SANTANDER", "BUCARAMANGA", "607"),
            ("SANTANDER", "OTRO", "608"),
        ]);

        Assert.Null(tabla.Resolver("SANTANDER", null));
        Assert.Equal("607", tabla.Resolver("SANTANDER", "BUCARAMANGA"));
    }

    [Fact]
    public void Departamento_conocido_no_cae_al_municipio_homonimo()
    {
        // CALI no está en ANTIOQUIA: no se toma el prefijo del homónimo de otro departamento,
        // se usa el del departamento (unívoco).
        Assert.Equal("604", Tabla.Resolver("ANTIOQUIA", "CALI"));
    }

    [Fact]
    public void Departamento_desconocido_cae_al_municipio()
    {
        Assert.Equal("602", Tabla.Resolver("NO EXISTE", "CALI"));
    }
}

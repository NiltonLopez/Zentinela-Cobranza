using Zentinela.Infrastructure.SQLServer.Services.Shared;

namespace Zentinela.Infrastructure.Tests.Shared;

/// <summary>
/// Filas solo-departamento (las de PrefijosDepartamento, que GeneradorSqlCargue.ConsultarPrefijos
/// suma a PrefijoMunicipio): resuelven nombres de departamento que PrefijoMunicipio escribe
/// distinto, sin volver ambiguo ningún departamento cuyo prefijo coincide.
/// </summary>
public class PrefijosFijoDepartamentoTests
{
    [Fact]
    public void Departamento_escrito_distinto_se_resuelve_por_la_fila_solo_departamento()
    {
        var sinDepartamentos = PrefijosFijo.Cargar([("N. DE SANTANDER", "CUCUTA", "607")]);
        var conDepartamentos = PrefijosFijo.Cargar([("N. DE SANTANDER", "CUCUTA", "607"), ("NORTE DE SANTANDER", "", "607")]);

        Assert.Null(sinDepartamentos.Resolver("NORTE DE SANTANDER", null));
        Assert.Equal("607", conDepartamentos.Resolver("NORTE DE SANTANDER", null));
        Assert.Equal("607", conDepartamentos.Resolver("Norte de Santander", "CUCUTA"));   // el municipio ya no está en ese depto: por depto
    }

    [Fact]
    public void Mismo_departamento_con_el_mismo_prefijo_sigue_siendo_univoco()
    {
        var tabla = PrefijosFijo.Cargar([("ANTIOQUIA", "MEDELLIN", "604"), ("ANTIOQUIA", "BELLO", "604"), ("ANTIOQUIA", "", "604")]);

        Assert.Equal("604", tabla.Resolver("ANTIOQUIA", null));
        Assert.Equal("604", tabla.Resolver("antioquia", "BELLO"));
    }

    [Fact]
    public void Si_las_dos_tablas_difirieran_no_se_adivina()
    {
        var tabla = PrefijosFijo.Cargar([("META", "VILLAVICENCIO", "608"), ("META", "", "601")]);

        Assert.Null(tabla.Resolver("META", null));
        Assert.Equal("608", tabla.Resolver("META", "VILLAVICENCIO"));   // el par exacto sigue mandando
    }
}

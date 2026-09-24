using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// Extensión genérica <see cref="DefinicionFuenteCargue.ColumnaClaveTitular"/> (Fase 2,
/// CamaraEmpresa): todo el SQL que compara, inserta, purga o registra reversión por titular usa la
/// columna de la definición; las fuentes de siempre siguen con [TitularID].
/// </summary>
public class GeneradorSqlClaveTitularTests
{
    private static readonly DefinicionFuenteCargue Empresa = CatalogoCargueFuentes.CamaraEmpresa;

    [Fact]
    public void Por_defecto_la_clave_es_TitularID()
    {
        var sura = CatalogoCargueFuentes.Sura;
        Assert.Equal("TitularID", sura.ColumnaClaveTitular);
        Assert.True(sura.ClaveEsTitular);
        Assert.Same(CatalogoCargueFuentes.AliasTitularId, sura.AliasDeClave);
        Assert.Equal("[TitularID]", GeneradorSqlCargue.ColumnaTitular(sura));
        Assert.StartsWith("INSERT INTO [dbo].[Sura] ([TitularID], ", GeneradorSqlCargue.Insertar(sura).TrimStart());
    }

    [Fact]
    public void CamaraEmpresa_usa_NitEmpresa_en_todo_el_SQL()
    {
        Assert.False(Empresa.ClaveEsTitular);
        Assert.Equal("[NitEmpresa]", GeneradorSqlCargue.ColumnaTitular(Empresa));

        var insertar = GeneradorSqlCargue.Insertar(Empresa);
        Assert.Contains("INSERT INTO [dbo].[CamaraEmpresa] ([NitEmpresa], [NumeroTelefono], [Email], [EstadoEnCamara], [FechaConsulta])", insertar);
        Assert.Contains("CONVERT(varchar(30), inserted.[NitEmpresa])", insertar);
        Assert.Contains("SELECT c.TitularID, c.[NumeroTelefono]", insertar);   // en #Carga la clave se llama TitularID

        foreach (var sql in new[] { GeneradorSqlCargue.MarcarImpacto(Empresa), GeneradorSqlCargue.Refrescar(Empresa),
                                    GeneradorSqlCargue.PurgarVacias(Empresa)!, GeneradorSqlCargue.ConsultarImpacto(Empresa) })
        {
            Assert.Contains("s.[NitEmpresa] = c.TitularID", sql);
            Assert.DoesNotContain("[TitularID]", sql);
        }
        Assert.Contains("deleted.[NitEmpresa]", GeneradorSqlCargue.Refrescar(Empresa));
        Assert.Contains("deleted.[NitEmpresa]", GeneradorSqlCargue.PurgarVacias(Empresa));
        Assert.Equal("NitEmpresa", GeneradorSqlCargue.ColumnasMuestra(Empresa)[0]);
    }

    [Fact]
    public void Clave_que_no_es_titular_no_puede_crear_Titular_ni_relaciones()
    {
        GeneradorSqlCargue.ValidarDefinicion(Empresa);   // la real es válida

        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(Empresa with { CreaTitular = true }));
        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(Empresa with { CreaTitularCartera = true }));
        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(Empresa with { TitularIdComoTexto = true }));
    }

    [Theory]
    [InlineData("Nit Empresa")]
    [InlineData("NitEmpresa;DROP")]
    public void Clave_con_identificador_invalido_se_rechaza(string clave)
    {
        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(Empresa with { ColumnaClaveTitular = clave }));
    }

    [Fact]
    public void Columna_de_dato_con_el_nombre_de_la_clave_se_rechaza()
    {
        var def = Empresa with
        {
            ColumnasDato = [.. Empresa.ColumnasDato, new ColumnaFuenteCargue("NitEmpresa", ["otro"], TipoColumnaCargue.Entero)],
        };
        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(def));
    }
}

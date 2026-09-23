using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

namespace Zentinela.Infrastructure.Tests.CargueFuentes;

/// <summary>
/// El SQL que el generador emite para Sura. Cada test fija una regla cuya violación se
/// notaría recién en producción: duplicar la tabla en cada carga, hashes de reversión que
/// colisionan o un INSERT posicional que se desalinea al agregar una columna.
/// </summary>
public class GeneradorSqlCargueTests
{
    private static readonly DefinicionFuenteCargue Sura = CatalogoCargueFuentes.Sura;

    private const string ColumnasSura =
        "[NumeroCelular], [Email], [NIT], [Empresa], [TipoAfiliado], [EstadoAfiliacion], [UltimaFechaConsulta]";

    [Fact]
    public void Insertar_lleva_lista_de_columnas_explicita()
    {
        var sql = GeneradorSqlCargue.Insertar(Sura);

        Assert.Contains(
            $"INSERT INTO [dbo].[Sura] ([TitularID], {ColumnasSura}, [EstadoEnSura], [FechaConsulta])", sql);
        Assert.Contains("c.[NumeroCelular], c.[Email]", sql);
        Assert.Contains("@Sello", sql);
        Assert.Contains("WHERE c.Refrescada = 0", sql);
    }

    [Fact]
    public void Clave_de_contenido_es_NULL_safe_con_INTERSECT()
    {
        var marcar = GeneradorSqlCargue.MarcarImpacto(Sura);
        var refrescar = GeneradorSqlCargue.Refrescar(Sura);

        var esperado = "EXISTS (SELECT s.[NumeroCelular], s.[Email], s.[NIT], s.[Empresa], s.[TipoAfiliado], "
                     + "s.[EstadoAfiliacion], s.[UltimaFechaConsulta] INTERSECT SELECT c.[NumeroCelular], c.[Email], "
                     + "c.[NIT], c.[Empresa], c.[TipoAfiliado], c.[EstadoAfiliacion], c.[UltimaFechaConsulta])";
        Assert.Contains(esperado, marcar);
        Assert.Contains(esperado, refrescar);

        // Con '=' toda fila con algún NULL quedaría como "nueva" en cada carga.
        Assert.DoesNotContain("s.[Email] = c.[Email]", marcar);
        Assert.DoesNotContain("s.[Email] = c.[Email]", refrescar);
    }

    [Fact]
    public void Hash_de_reversion_usa_centinela_por_columna()
    {
        var hash = GeneradorSqlCargue.HashContenido(Sura, "deleted");

        Assert.StartsWith("HASHBYTES('SHA2_256', CONCAT_WS(N'|', ", hash);
        foreach (var c in Sura.ColumnasDato)
            Assert.Contains($"ISNULL(CONVERT(nvarchar(400), deleted.[{c.Nombre}]), N'~')", hash);
    }

    [Fact]
    public void Hash_con_una_sola_columna_no_usa_CONCAT_WS()
    {
        // CONCAT_WS exige al menos dos valores (SIMIT tiene solo Placa).
        var una = Sura with { ColumnasDato = [new ColumnaFuenteCargue("Placa", ["placa"], TipoColumnaCargue.Placa, 10)] };

        var hash = GeneradorSqlCargue.HashContenido(una, "inserted");

        Assert.Equal("HASHBYTES('SHA2_256', ISNULL(CONVERT(nvarchar(400), inserted.[Placa]), N'~'))", hash);
    }

    [Fact]
    public void Refrescar_y_purgar_registran_reversion()
    {
        var refrescar = GeneradorSqlCargue.Refrescar(Sura);
        var purgar = GeneradorSqlCargue.PurgarVacias(Sura)!;
        var insertar = GeneradorSqlCargue.Insertar(Sura);

        const string destino = "INTO dbo.CargueFuenteReversion (EjecucionID, TitularID, HashContenido, Accion, FechaAnterior)";
        Assert.Contains(destino, refrescar);
        Assert.Contains(destino, purgar);
        Assert.Contains(destino, insertar);
        Assert.Contains("'U', deleted.[FechaConsulta]", refrescar);
        Assert.Contains("'I', NULL", insertar);
        Assert.Contains("'D', deleted.[FechaConsulta]", purgar);
        Assert.Contains("SET s.[FechaConsulta] = @Sello", refrescar);
    }

    [Fact]
    public void Refrescadas_se_cuentan_del_lado_de_Carga()
    {
        Assert.Equal("SELECT COUNT(*) FROM #Carga WHERE Refrescada = 1;", GeneradorSqlCargue.ContarRefrescadas);
    }

    [Fact]
    public void Tabla_de_trabajo_tipa_columnas_segun_definicion()
    {
        var ddl = GeneradorSqlCargue.CrearTablaCarga(Sura);

        Assert.Contains("[NumeroCelular] varchar(10) COLLATE DATABASE_DEFAULT NULL", ddl);
        Assert.Contains("[Email] varchar(255) COLLATE DATABASE_DEFAULT NULL", ddl);
        Assert.Contains("[NIT] bigint NULL", ddl);
        Assert.Contains("[UltimaFechaConsulta] date NULL", ddl);
        Assert.Contains("[EstadoEnSura] bit NOT NULL", ddl);
        // En tempdb el nombre de un constraint es global: con nombre, dos cargues simultáneos chocarían.
        Assert.DoesNotContain("CONSTRAINT", ddl);
    }

    [Fact]
    public void Relaciones_solo_con_pares_activos()
    {
        Assert.Contains("lc.Activo = 1", GeneradorSqlCargue.InsertarRelaciones);
        Assert.Contains("INSERT INTO dbo.Titular_Cartera (TitularID, LiderID, CarteraID)", GeneradorSqlCargue.InsertarRelaciones);
    }

    [Fact]
    public void Nombres_rotos_no_se_buscan_con_LIKE()
    {
        // Con collation insensible a acentos, LIKE '%Ã%' matchea cualquier 'A'.
        Assert.DoesNotContain("LIKE", GeneradorSqlCargue.ConsultarTitulares);
        Assert.DoesNotContain("LIKE", GeneradorSqlCargue.RepararNombres);
    }

    [Fact]
    public void Sello_sale_del_servidor_SQL_segun_tipo_de_destino()
    {
        Assert.Equal("SELECT CAST(GETDATE() AS date);", GeneradorSqlCargue.LeerSello(Sura));
        Assert.Equal("SELECT GETDATE();", GeneradorSqlCargue.LeerSello(Sura with { TipoFecha = TipoFechaSello.DateTime }));
    }

    [Fact]
    public void TitularID_texto_convierte_el_lado_de_Carga()
    {
        var texto = Sura with { TitularIdComoTexto = true };

        Assert.Contains("s.[TitularID] = CONVERT(varchar(30), c.TitularID)", GeneradorSqlCargue.MarcarImpacto(texto));
        Assert.Contains("SELECT CONVERT(varchar(30), c.TitularID), c.[NumeroCelular]", GeneradorSqlCargue.Insertar(texto));
    }

    [Fact]
    public void Sin_flag_no_hay_politica_de_vacias()
    {
        var sinFlag = Sura with { ColumnaFlag = null };

        Assert.Null(GeneradorSqlCargue.PurgarVacias(sinFlag));
        Assert.DoesNotContain("Vacia = 1", GeneradorSqlCargue.MarcarImpacto(sinFlag));
        Assert.DoesNotContain("EstadoEnSura", GeneradorSqlCargue.Insertar(sinFlag));
    }

    [Theory]
    [InlineData("Email; DROP TABLE Titular")]
    [InlineData("Nombre")]   // reservada: choca con #Carga.Nombre
    [InlineData("1Columna")]
    public void Definicion_con_identificador_invalido_se_rechaza(string columna)
    {
        var mala = Sura with { ColumnasDato = [.. Sura.ColumnasDato, new ColumnaFuenteCargue(columna, ["x"], TipoColumnaCargue.Texto, 10)] };

        Assert.Throws<InvalidOperationException>(() => GeneradorSqlCargue.ValidarDefinicion(mala));
    }
}

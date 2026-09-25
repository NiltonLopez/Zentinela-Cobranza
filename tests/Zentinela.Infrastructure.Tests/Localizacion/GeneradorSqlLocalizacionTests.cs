using System.Text.RegularExpressions;
using Zentinela.Application;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// El SQL del precálculo de los Informes de Localización (ADR-040). Cada test fija una regla cuya
/// violación se notaría recién en producción: cifras distintas a las del Calificador, un corte que no
/// es determinista, NuevaEps sin cruzar, CamaraEmpresa contada por titular o las tablas de prueba
/// escribiendo en producción.
/// </summary>
public class GeneradorSqlLocalizacionTests
{
    private static FuenteLocalizacion F(string clave) => CatalogoFuentesLocalizacion.Buscar(clave)!;

    // ── Fuentes que entran ───────────────────────────────────────────────────

    [Fact]
    public void Recorre_las_21_fuentes_del_panel_y_no_CamaraEmpresa()
    {
        Assert.Equal(21, GeneradorSqlLocalizacion.Fuentes.Count);
        Assert.All(GeneradorSqlLocalizacion.Fuentes, f => Assert.True(f.EnPanelCalificador));

        var sql = Sql.Precalculo();
        foreach (var f in GeneradorSqlLocalizacion.Fuentes)
            Assert.Contains($"INTO #F_{f.Clave}\n", sql);
        Assert.DoesNotContain("CamaraEmpresa", sql);
        Assert.DoesNotContain("NitEmpresa = ", sql);
    }

    // ── Última fila por titular ──────────────────────────────────────────────

    [Theory]
    [InlineData("Fosyga", "dbo.Fosyga", "FechaConsulta")]
    [InlineData("DatosClaro", "dbo.DatosClaro", "FechaInsercion")]
    [InlineData("RUES", "dbo.RUES", "FechaConsulta")]
    public void Ultima_fila_usa_la_tabla_y_la_fecha_del_catalogo(string clave, string tabla, string fecha)
    {
        var sql = Sql.UltimaFila(F(clave));

        Assert.Contains($"FROM {tabla} s", sql);
        Assert.Contains($"CAST(s.{fecha} AS date) AS Fecha", sql);
        Assert.Contains("WHERE x.rn = 1", sql);
        Assert.Contains("INNER JOIN #Tit t ON t.TitularID = s.TitularID", sql);
    }

    [Fact]
    public void Desempate_determinista_en_la_misma_fecha_gana_la_fila_con_dato()
    {
        // El Calificador ordena solo por fecha (empates arbitrarios). Aquí: fecha, luego Enc y Con.
        var sql = Sql.UltimaFila(F("Coosalud"));
        Assert.Contains("ORDER BY s.FechaConsulta DESC, e.Enc DESC, e.Con DESC", sql);
        Assert.Contains("PARTITION BY t.TitularID", sql);
    }

    [Fact]
    public void Predicado_encontrado_es_el_del_catalogo()
    {
        var f = F("SaviaSalud");
        var sql = Sql.UltimaFila(f);

        Assert.Contains($"CAST(CASE WHEN {f.PredicadoEncontrado} THEN 1 ELSE 0 END AS tinyint) AS Enc", sql);
        // SaviaSalud.NumeroTelefono NO cuenta (la BaseMadre no lo proyecta): solo NumeroCelular.
        Assert.DoesNotContain("s.NumeroTelefono", sql);
        Assert.Contains("s.NumeroCelular", sql);
    }

    [Fact]
    public void Fuente_sin_celular_ni_email_tiene_Con_0()
    {
        var sql = Sql.UltimaFila(F("Fosyga"));
        Assert.Contains("CAST(0 AS tinyint) AS Con", sql);
    }

    [Fact]
    public void NuevaEps_cruza_con_TRY_CAST_porque_guarda_el_TitularID_como_texto()
    {
        var sql = Sql.UltimaFila(F("NuevaEps"));
        Assert.Contains("INNER JOIN #Tit t ON t.TitularID = TRY_CAST(s.TitularID AS bigint)", sql);
        // La partición y la salida usan el TitularID numérico de #Tit, no el texto.
        Assert.Contains("SELECT t.TitularID,", sql);
        Assert.DoesNotContain("PARTITION BY s.TitularID", sql);
    }

    [Fact]
    public void Todas_las_consultas_pesadas_llevan_MAXDOP_4()
    {
        var sql = Sql.Precalculo();
        var selectsInto = Regex.Matches(sql, @"\bINTO #(F_|Alc|AlcS|LT|X|RP|RL)\b").Count;
        var opciones = Regex.Matches(sql, @"OPTION \(MAXDOP 4(, HASH JOIN)?\);").Count;
        Assert.True(opciones >= selectsInto, $"{opciones} OPTION para {selectsInto} SELECT INTO");
        Assert.DoesNotContain("MAXDOP 0", sql);
        Assert.Equal(21, Regex.Matches(sql, @"OPTION \(MAXDOP 4, HASH JOIN\);\nCREATE UNIQUE CLUSTERED INDEX IX_F").Count);
    }

    // ── Contacto: complemento exacto de SinContacto del Calificador ──────────

    /// <summary>
    /// Columnas que el result set 1 del Calificador cruza en SinContacto (15 celulares y 11 emails de
    /// <c>fn_BaseMadreParaTitulares</c>), traducidas a tabla.columna origen.
    /// </summary>
    private static readonly Dictionary<string, string[]> ColumnasContactoCalificador = new()
    {
        ["Cajacopi"]   = ["NumeroTelefonoCelular", "Email"],
        ["Camara"]     = ["NumeroTelefono", "Email"],
        ["SaludTotal"] = ["NumeroTelefono", "Email"],
        ["Positiva"]   = ["NumeroTelefonoCelular", "Email"],
        ["Tigo"]       = ["NumeroTelefono", "Email"],
        ["TigoUne"]    = ["NumeroTelefono", "Email"],
        ["Coosalud"]   = ["NumeroTelefono", "Email"],
        ["SaviaSalud"] = ["NumeroCelular"],
        ["Sura"]       = ["NumeroCelular", "Email"],
        ["SOS"]        = ["Telefono"],
        ["DatosClaro"] = ["NumeroTelefono", "Email"],
        ["MutualSer"]  = ["NumeroTelefonoCelular", "NumeroTelefonoCelular2", "Email"],
        ["MiVacuna"]   = ["Celular", "Email"],
        ["NuevaEps"]   = ["NumeroTelefono"],
    };

    [Fact]
    public void Contacto_usa_exactamente_las_columnas_de_SinContacto_del_Calificador()
    {
        foreach (var f in GeneradorSqlLocalizacion.Fuentes)
        {
            var columnas = f.PredicadoContacto is null
                ? []
                : Regex.Matches(f.PredicadoContacto, @"s\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ToArray();
            var esperado = ColumnasContactoCalificador.GetValueOrDefault(f.Clave, []);
            Assert.True(esperado.OrderBy(x => x).SequenceEqual(columnas.OrderBy(x => x)),
                $"{f.Clave}: esperado [{string.Join(", ", esperado)}], catálogo [{string.Join(", ", columnas)}]");
        }
        Assert.Equal(15 + 11, ColumnasContactoCalificador.Values.Sum(v => v.Length));
    }

    [Fact]
    public void Contacto_cruzado_toma_el_maximo_de_Con_de_todas_las_fuentes()
    {
        var sql = Sql.Precalculo();
        Assert.Contains("MAX(u.Con)            AS Con", sql);
        foreach (var f in GeneradorSqlLocalizacion.Fuentes)
            Assert.Contains($"SELECT TitularID, Con, CAST({(f.Estado == EstadoFuente.Funcional ? 1 : 0)} AS tinyint) AS Func, Fecha FROM #F_{f.Clave}", sql);
    }

    // ── Tablas destino ───────────────────────────────────────────────────────

    [Fact]
    public void Produccion_escribe_en_dbo()
    {
        var sql = Sql.Precalculo();
        Assert.Contains("INSERT INTO dbo.LocalizacionResumenFuente (CorteID,", sql);
        Assert.Contains("INSERT INTO dbo.LocalizacionResumenFuenteLider (CorteID,", sql);
        Assert.Contains("INSERT INTO dbo.LocalizacionResumenPar (CorteID,", sql);
        Assert.Contains("INSERT INTO dbo.LocalizacionResumenLider (CorteID,", sql);
        Assert.Contains("BEGIN TRANSACTION;", sql);
        Assert.Contains("COMMIT TRANSACTION;", sql);
    }

    [Fact]
    public void Con_prefijo_temporal_no_toca_ninguna_tabla_de_resumen_permanente()
    {
        var sql = Sql.Precalculo("#");
        Assert.Contains("INSERT INTO #LocalizacionResumenFuente (CorteID,", sql);
        Assert.Contains("INSERT INTO #LocalizacionResumenLider (CorteID,", sql);
        Assert.DoesNotContain("dbo.LocalizacionResumen", sql);
        // Nada de DDL ni DML sobre tablas permanentes: solo lecturas de dbo.
        Assert.DoesNotMatch(@"(INSERT INTO|UPDATE|DELETE FROM|CREATE TABLE|DROP TABLE IF EXISTS)\s+dbo\.", sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dbo")]
    [InlineData("x; DROP TABLE dbo.Titular; --")]
    [InlineData("otra.")]
    public void Prefijo_no_permitido_se_rechaza(string prefijo)
    {
        Assert.Throws<ArgumentException>(() => Sql.Precalculo(prefijo));
    }

    // ── Alcance y universos ──────────────────────────────────────────────────

    [Fact]
    public void Alcance_son_los_pares_activos_con_titular_existente()
    {
        var sql = Sql.Precalculo();
        Assert.Contains("lc.Activo = 1", sql);
        Assert.Contains("c.Activa = 1", sql);
        // La BaseMadre del Calificador hace INNER JOIN a Titular: sin esto el Total no coincide.
        Assert.Contains("WHERE EXISTS (SELECT 1 FROM dbo.Titular ti WHERE ti.NumeroDocumento = tc.TitularID)", sql);
        Assert.Contains("lc.EsExterna = 0", sql);
        Assert.Contains("'BASE GENERAL'", sql);
    }

    [Fact]
    public void Lider_cuenta_titulares_distintos_y_LiderID_0_es_toda_la_base()
    {
        var sql = Sql.Precalculo();
        Assert.Contains("GROUP BY s.LiderID, s.TitularID", sql);
        Assert.Contains("SELECT 0, g.TitularID", sql);
        // Saldo por (TitularID, CarteraID): UltimoEstadoTitular es por cartera.
        Assert.Contains("u.TitularID = a.TitularID AND u.CarteraID = a.CarteraID", sql);
    }

    [Fact]
    public void Viejos_cuentan_buscados_sin_fecha()
    {
        var sql = Sql.Agregado(F("Fosyga"), porLider: false);
        Assert.Contains("f.TitularID IS NOT NULL AND (f.Fecha IS NULL OR f.Fecha < @Lim90)", sql);
        Assert.Contains("GROUP BY a.LiderID, a.CarteraID", sql);
        Assert.Contains("'Fosyga'", sql);

        var porLider = Sql.Agregado(F("Fosyga"), porLider: true);
        Assert.Contains("FROM #LT a", porLider);
        Assert.Contains("GROUP BY a.LiderID\n", porLider);
    }

    [Fact]
    public void Temporales_sin_constraints_con_nombre()
    {
        // En tempdb el nombre de un constraint es global: dos cortes a la vez chocarían.
        var sql = Sql.Precalculo();
        Assert.DoesNotContain("CONSTRAINT", sql);
    }
}

/// <summary>
/// El generador con saltos de línea normalizados: los raw strings toman los del archivo fuente, que con
/// core.autocrlf pueden ser CRLF en otra máquina.
/// </summary>
internal static class Sql
{
    private static string N(string s) => s.Replace("\r\n", "\n");
    public static string Precalculo(string prefijo = GeneradorSqlLocalizacion.PrefijoProduccion) => N(GeneradorSqlLocalizacion.Precalculo(prefijo));
    public static string UltimaFila(FuenteLocalizacion f) => N(GeneradorSqlLocalizacion.UltimaFila(f));
    public static string Agregado(FuenteLocalizacion f, bool porLider) => N(GeneradorSqlLocalizacion.Agregado(f, porLider));
}

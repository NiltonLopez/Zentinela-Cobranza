using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Etapa 4 (ADR-040): elección del corte de cada semana para la tendencia, ranking de asesores y el bloque de
/// asesores del precálculo. El SQL se corrió además contra tablas #temp en la BD (documentado en ADR-040).
/// </summary>
public class TendenciaAsesoresTests
{
    // ════════════════════════════════════════════════════════════════════════
    //  Tendencia: último corte OK de cada semana
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2026-09-26", "2026-09-21")]   // sábado → lunes
    [InlineData("2026-09-27", "2026-09-21")]   // domingo → lunes de ESA semana (no el siguiente)
    [InlineData("2026-09-21", "2026-09-21")]   // lunes → el mismo
    [InlineData("2026-09-21 23:59", "2026-09-21")]
    [InlineData("2026-01-01", "2025-12-29")]   // cruza el año
    public void Inicio_de_semana_es_el_lunes(string fecha, string lunes)
    {
        Assert.Equal(DateTime.Parse(lunes), TendenciaLocalizacion.InicioSemana(DateTime.Parse(fecha)));
    }

    [Fact]
    public void Toma_el_ultimo_corte_de_cada_semana_y_descarta_lo_viejo()
    {
        var hoy = new DateTime(2026, 9, 26, 15, 0, 0);   // sábado
        (int, DateTime)[] cortes =
        [
            (1, new DateTime(2026, 8, 25, 13, 0, 0)),     // fuera: 5 semanas atrás con semanas = 4
            (2, new DateTime(2026, 9, 1, 13, 0, 0)),      // semana del 31/08
            (3, new DateTime(2026, 9, 3, 13, 0, 0)),      // semana del 31/08 (el último de esa semana)
            (4, new DateTime(2026, 9, 14, 13, 0, 0)),     // semana del 14/09
            (5, new DateTime(2026, 9, 21, 13, 0, 0)),     // semana actual
            (7, new DateTime(2026, 9, 24, 15, 40, 0)),    // semana actual, tras un cargue
            (6, new DateTime(2026, 9, 24, 13, 0, 0)),     // semana actual: CorteID mayor pero fecha menor, pierde
        ];

        var s = TendenciaLocalizacion.CortesSemanales(cortes, hoy, semanas: 4);

        Assert.Equal([3, 4, 7], s.Select(c => c.CorteID));   // la semana del 07/09 no tuvo cortes: no se inventa
        Assert.Equal([new DateTime(2026, 8, 31), new DateTime(2026, 9, 14), new DateTime(2026, 9, 21)], s.Select(c => c.InicioSemana));
        Assert.Equal(new DateTime(2026, 8, 31), TendenciaLocalizacion.Desde(hoy, 4));
    }

    [Fact]
    public void A_igual_fecha_gana_el_CorteID_mayor_y_nada_del_futuro()
    {
        var hoy = new DateTime(2026, 9, 26);
        var f = new DateTime(2026, 9, 22, 13, 0, 0);
        var s = TendenciaLocalizacion.CortesSemanales([(10, f), (11, f), (12, new DateTime(2026, 9, 28, 13, 0, 0))], hoy, 2);
        Assert.Equal(11, Assert.Single(s).CorteID);
    }

    [Fact]
    public void Semana_anterior_es_el_ultimo_corte_de_la_semana_pasada()
    {
        var hoy = new DateTime(2026, 9, 23);   // miércoles
        (int, DateTime)[] cortes =
        [
            (1, new DateTime(2026, 9, 14, 13, 0, 0)),
            (2, new DateTime(2026, 9, 18, 13, 0, 0)),
            (3, new DateTime(2026, 9, 22, 13, 0, 0)),
        ];
        Assert.Equal(2, TendenciaLocalizacion.SemanaAnterior(cortes, hoy)!.CorteID);
        Assert.Null(TendenciaLocalizacion.SemanaAnterior([(3, new DateTime(2026, 9, 22, 13, 0, 0))], hoy));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Ranking de asesores
    // ════════════════════════════════════════════════════════════════════════

    private sealed record A(string Nombre, MetricasAsesor M);

    private static MetricasAsesor M(int tit, int contacto, int gestion = 0, int conDato = 0, int conSaldo = 0, int sinContactoSaldo = 0) =>
        new(tit, conDato, conSaldo, sinContactoSaldo, sinContactoSaldo * 1000m, gestion, contacto);

    [Fact]
    public void Contactabilidad_por_defecto_con_minimo_de_titulares()
    {
        A[] asesores =
        [
            new("Ana",   M(100, 40)),   // 40 %
            new("Beto",  M(200, 90)),   // 45 %
            new("Carla", M(10, 10)),    // 100 %, pero con 10 titulares no entra (mínimo 30)
            new("Dora",  M(50, 20)),    // 40 %: empata con Ana
            new("Eva",   M(0, 0)),      // sin titulares: ni ranking ni lista aparte
        ];
        Assert.True(RankingAsesoresCalculo.TryParseCriterio(null, out var c));
        Assert.Equal(CriterioRankingAsesor.Contactabilidad, c);

        var (ranking, pocos) = RankingAsesoresCalculo.Rankear(asesores, a => a.M, a => a.Nombre, c, minTitulares: 30);

        Assert.Equal(["Beto", "Ana", "Dora"], ranking.Select(f => f.Item.Nombre));
        Assert.Equal([1, 2, 2], ranking.Select(f => f.Posicion));
        Assert.Equal([45m, 40m, 40m], ranking.Select(f => f.Valor!.Value));
        Assert.Equal("Carla", Assert.Single(pocos).Nombre);
    }

    [Fact]
    public void Criterios_y_sin_contacto_con_saldo_menos_es_mejor_y_null_sin_saldo()
    {
        var m = M(200, contacto: 50, gestion: 150, conDato: 160, conSaldo: 100, sinContactoSaldo: 30);
        Assert.Equal(25m, RankingAsesoresCalculo.Valor(m, CriterioRankingAsesor.Contactabilidad));
        Assert.Equal(75m, RankingAsesoresCalculo.Valor(m, CriterioRankingAsesor.Gestion));
        Assert.Equal(80m, RankingAsesoresCalculo.Valor(m, CriterioRankingAsesor.ConDato));
        Assert.Equal(15m, RankingAsesoresCalculo.Valor(m, CriterioRankingAsesor.SinContactoSaldo));
        // Cartera sin saldo en UltimoEstadoTitular: "—", no 0 % (no gana el ranking por falta de dato).
        Assert.Null(RankingAsesoresCalculo.Valor(M(200, 50), CriterioRankingAsesor.SinContactoSaldo));

        A[] asesores = [new("Ana", M(100, 0, conSaldo: 50, sinContactoSaldo: 30)), new("Beto", M(100, 0, conSaldo: 50, sinContactoSaldo: 10)), new("Sin", M(100, 0))];
        var (ranking, _) = RankingAsesoresCalculo.Rankear(asesores, a => a.M, a => a.Nombre, CriterioRankingAsesor.SinContactoSaldo, 30);
        Assert.True(RankingAsesoresCalculo.MenorEsMejor(CriterioRankingAsesor.SinContactoSaldo));
        Assert.Equal(["Beto", "Ana", "Sin"], ranking.Select(f => f.Item.Nombre));
        Assert.Null(ranking[2].Valor);

        Assert.True(RankingAsesoresCalculo.TryParseCriterio("gestion", out var g) && g == CriterioRankingAsesor.Gestion);
        Assert.False(RankingAsesoresCalculo.TryParseCriterio("Recaudo", out _));
    }

    [Fact]
    public void Metricas_de_asesor_se_suman_entre_pares()
    {
        var s = MetricasAsesor.Sumar([M(10, 2, 3, 4, 5, 1), M(20, 4, 6, 8, 10, 2)]);
        Assert.Equal(new MetricasAsesor(30, 12, 15, 3, 3000m, 9, 6), s);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Bloque de asesores del precálculo
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Precalculo_agrega_por_par_y_asesor_con_el_ultimo_gestor()
    {
        var sql = Sql.Precalculo();
        Assert.Contains("u.AgenteID, u.FechaUltimaGestion, u.UltimoPerfil", sql);
        Assert.Contains("GROUP BY s.LiderID, s.CarteraID, s.AgenteID", sql);
        Assert.Contains("WHERE s.AgenteID IS NOT NULL", sql);
        Assert.Contains("DATEADD(DAY, -@DiasGestion, @Hoy)", sql);
        Assert.Contains("OPENJSON(@PerfilesContacto)", sql);
        // Tercero / familiar cuentan como contacto, como en el impacto de los envíos.
        Assert.Contains("n.Perfil LIKE '%TERCERO%'", sql);
        Assert.Contains("n.Perfil LIKE '%FAMILIAR%'", sql);
        // La tabla es opcional: un script aplicado antes de la etapa 4 no tumba el corte.
        Assert.Contains("IF OBJECT_ID('dbo.LocalizacionResumenAsesor', 'U') IS NOT NULL\n    INSERT INTO dbo.LocalizacionResumenAsesor (CorteID,", sql);
        Assert.Contains("#PerfC, #RA", sql);
    }

    [Fact]
    public void Precalculo_contra_temporales_mira_tempdb_para_la_tabla_de_asesores()
    {
        var sql = Sql.Precalculo("#");
        Assert.Contains("IF OBJECT_ID('tempdb..#LocalizacionResumenAsesor', 'U') IS NOT NULL", sql);
        Assert.DoesNotContain("dbo.LocalizacionResumenAsesor", sql);
        Assert.Equal("OBJECT_ID('tempdb..##LocalizacionResumenAsesor', 'U') IS NOT NULL",
            GeneradorSqlLocalizacion.TablasDestino.ConPrefijo("##").ExisteAsesor);
    }
}

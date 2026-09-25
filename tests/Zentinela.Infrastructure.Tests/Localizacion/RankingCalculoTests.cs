using Zentinela.Application.Settings;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Índice, orden, empates y recorte para el Líder de los rankings de localización (ADR-040), más la
/// exclusión de relaciones externas y "BASE GENERAL" tal como la aplica el servicio de lecturas.
/// </summary>
public class RankingCalculoTests
{
    private static readonly InformesLocalizacionSettings Pesos = new();   // 0,50 / 0,25 / 0,25

    private static MetricasLocalizacion M(int total, int contacto, int cobertura, int frescos,
                                          int sinContactoConSaldo = 0, decimal saldoSinContacto = 0) =>
        new(total, contacto, 0, cobertura, cobertura, frescos, total, sinContactoConSaldo, saldoSinContacto, 0);

    // ── Índice ───────────────────────────────────────────────────────────────

    [Fact]
    public void Indice_es_el_promedio_ponderado_de_contacto_cobertura_y_frescura()
    {
        // 0,5 × 80 % + 0,25 × 60 % + 0,25 × 40 % = 40 + 15 + 10 = 65
        Assert.Equal(65m, CalculoLocalizacion.Indice(M(100, 80, 60, 40), Pesos));
    }

    [Fact]
    public void Indice_normaliza_pesos_que_no_suman_1()
    {
        var dobles = new InformesLocalizacionSettings { PesoContacto = 1m, PesoCobertura = 0.5m, PesoFrescura = 0.5m };
        Assert.Equal(65m, CalculoLocalizacion.Indice(M(100, 80, 60, 40), dobles));
    }

    [Fact]
    public void Indice_de_un_universo_vacio_es_null()
    {
        Assert.Null(CalculoLocalizacion.Indice(MetricasLocalizacion.Vacias, Pesos));
        Assert.Null(CalculoLocalizacion.Pct(5, 0));
    }

    [Fact]
    public void Indice_se_mantiene_entre_0_y_100()
    {
        Assert.Equal(100m, CalculoLocalizacion.Indice(M(10, 10, 10, 10), Pesos));
        Assert.Equal(0m, CalculoLocalizacion.Indice(M(10, 0, 0, 0), Pesos));
    }

    [Fact]
    public void Sumar_metricas_de_pares_es_campo_a_campo()
    {
        var s = MetricasLocalizacion.Sumar([M(100, 80, 60, 40, 5, 1000m), M(50, 10, 20, 30, 7, 500m)]);
        Assert.Equal(M(150, 90, 80, 70, 12, 1500m), s);
    }

    // ── Orden y empates ──────────────────────────────────────────────────────

    private sealed record L(int Id, string Nombre, MetricasLocalizacion M);

    private static IReadOnlyList<FilaRankeada<L>> Rankear(IEnumerable<L> lideres, CriterioRankingLocalizacion c) =>
        CalculoLocalizacion.Rankear(lideres, l => CalculoLocalizacion.ValorCriterio(l.M, c, Pesos), l => l.Nombre,
            CalculoLocalizacion.MenorEsMejor(c));

    [Fact]
    public void Ordena_de_mayor_a_menor_por_indice()
    {
        var r = Rankear([
            new L(1, "Ana", M(100, 50, 50, 50)),
            new L(2, "Beto", M(100, 90, 90, 90)),
            new L(3, "Carla", M(100, 70, 70, 70)),
        ], CriterioRankingLocalizacion.Indice);

        Assert.Equal([2, 3, 1], r.Select(f => f.Item.Id));
        Assert.Equal([1, 2, 3], r.Select(f => f.Posicion));
        Assert.Equal(90m, r[0].Valor);
    }

    [Fact]
    public void Sin_contacto_con_saldo_gana_el_que_menos_tiene()
    {
        var r = Rankear([
            new L(1, "Ana", M(100, 0, 0, 0, sinContactoConSaldo: 30)),
            new L(2, "Beto", M(100, 0, 0, 0, sinContactoConSaldo: 10)),
        ], CriterioRankingLocalizacion.SinContactoSaldo);

        Assert.True(CalculoLocalizacion.MenorEsMejor(CriterioRankingLocalizacion.SinContactoSaldo));
        Assert.Equal([2, 1], r.Select(f => f.Item.Id));
        Assert.Equal(10m, r[0].Valor);
    }

    [Fact]
    public void Sin_contacto_con_saldo_sin_datos_de_saldo_queda_sin_valor_y_al_final()
    {
        // Carteras cuyo saldo no llega a UltimoEstadoTitular (Delmar): 0 % sería "sin dato", no "el mejor".
        var sinSaldo = new MetricasLocalizacion(100, 10, 0, 0, 0, 0, ConSaldo: 0, SinContactoConSaldo: 0, 0, 0);
        Assert.Null(CalculoLocalizacion.PctSinContactoConSaldo(sinSaldo));

        var r = Rankear([
            new L(1, "Ana", sinSaldo),
            new L(2, "Beto", M(100, 0, 0, 0, sinContactoConSaldo: 40)),
        ], CriterioRankingLocalizacion.SinContactoSaldo);
        Assert.Equal([2, 1], r.Select(f => f.Item.Id));
        Assert.Null(r[1].Valor);
    }

    [Fact]
    public void Empates_a_un_decimal_comparten_posicion_y_la_siguiente_salta()
    {
        // 66,66 % y 66,67 % se ven igual en pantalla (66,7): empatan. Dentro del empate, por nombre.
        var r = Rankear([
            new L(1, "Zoe",   M(3, 2, 0, 0)),          // 66,67 %
            new L(2, "Ana",   M(10000, 6666, 0, 0)),   // 66,66 %
            new L(3, "Beto",  M(10, 9, 0, 0)),         // 90 %
            new L(4, "Carla", M(10, 1, 0, 0)),         // 10 %
        ], CriterioRankingLocalizacion.Contacto);

        Assert.Equal([3, 2, 1, 4], r.Select(f => f.Item.Id));
        Assert.Equal([1, 2, 2, 4], r.Select(f => f.Posicion));
    }

    [Fact]
    public void Sin_valor_va_al_final()
    {
        var r = Rankear([
            new L(1, "Ana", MetricasLocalizacion.Vacias),
            new L(2, "Beto", M(10, 1, 0, 0)),
        ], CriterioRankingLocalizacion.Contacto);

        Assert.Equal([2, 1], r.Select(f => f.Item.Id));
        Assert.Null(r[1].Valor);
    }

    [Theory]
    [InlineData(null, CriterioRankingLocalizacion.Indice)]
    [InlineData("contacto", CriterioRankingLocalizacion.Contacto)]
    [InlineData("SinContactoSaldo", CriterioRankingLocalizacion.SinContactoSaldo)]
    public void Criterio_se_parsea_sin_importar_mayusculas(string? texto, CriterioRankingLocalizacion esperado)
    {
        Assert.True(CalculoLocalizacion.TryParseCriterio(texto, out var c));
        Assert.Equal(esperado, c);
    }

    [Theory]
    [InlineData("costo")]   // "uso" y "retorno" existen desde la etapa 3
    [InlineData("99")]
    public void Criterio_desconocido_se_rechaza(string texto) =>
        Assert.False(CalculoLocalizacion.TryParseCriterio(texto, out _));

    // ── Vista del Líder: top + su propia fila ────────────────────────────────

    private static IReadOnlyList<FilaRankeada<L>> Diez() =>
        Rankear(Enumerable.Range(1, 10).Select(i => new L(i, $"Lider {i:00}", M(100, 100 - i * 5, 0, 0))),
                CriterioRankingLocalizacion.Contacto);

    [Fact]
    public void Lider_fuera_del_top_recibe_top_5_mas_su_fila_y_su_posicion()
    {
        var r = CalculoLocalizacion.RecortarParaLider(Diez(), l => l.Id == 8, top: 5);

        Assert.Equal([1, 2, 3, 4, 5, 8], r.Filas.Select(f => f.Item.Id));
        Assert.Equal(8, r.MiPosicion);
        Assert.Equal(10, r.Total);
        Assert.True(r.Recortado);
    }

    [Fact]
    public void Lider_dentro_del_top_no_se_duplica()
    {
        var r = CalculoLocalizacion.RecortarParaLider(Diez(), l => l.Id == 3, top: 5);

        Assert.Equal([1, 2, 3, 4, 5], r.Filas.Select(f => f.Item.Id));
        Assert.Equal(3, r.MiPosicion);
    }

    [Fact]
    public void Lider_nunca_recibe_la_lista_completa()
    {
        var r = CalculoLocalizacion.RecortarParaLider(Diez(), l => l.Id == 10, top: 5);
        Assert.True(r.Filas.Count < r.Total);
        Assert.Equal(6, r.Filas.Count);
    }

    [Fact]
    public void Empate_en_la_ultima_posicion_del_top_entra_completo()
    {
        var lideres = new[]
        {
            new L(1, "A", M(10, 10, 0, 0)), new L(2, "B", M(10, 9, 0, 0)), new L(3, "C", M(10, 8, 0, 0)),
            new L(4, "D", M(10, 7, 0, 0)), new L(5, "E", M(10, 6, 0, 0)), new L(6, "F", M(10, 6, 0, 0)),
            new L(7, "G", M(10, 1, 0, 0)),
        };
        var r = CalculoLocalizacion.RecortarParaLider(Rankear(lideres, CriterioRankingLocalizacion.Contacto), l => l.Id == 7, top: 5);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], r.Filas.Select(f => f.Item.Id));
        Assert.Equal(7, r.MiPosicion);
    }

    [Fact]
    public void Lider_sin_fila_en_el_ranking_no_tiene_posicion()
    {
        var r = CalculoLocalizacion.RecortarParaLider(Diez(), l => l.Id == 99, top: 5);
        Assert.Null(r.MiPosicion);
        Assert.Equal(5, r.Filas.Count);
    }

    // ── Exclusión de externas y BASE GENERAL ─────────────────────────────────

    [Fact]
    public void Carteras_sin_externas_suman_solo_los_pares_internos()
    {
        // Así arma el servicio el ranking de carteras con el interruptor apagado: la bandera Interna la
        // calcula el precálculo (EsExterna = 0 y ni líder ni cartera "BASE GENERAL").
        var pares = new[]
        {
            (Cartera: 22, Interna: true,  M: M(100, 50, 50, 50)),
            (Cartera: 22, Interna: false, M: M(100, 10, 10, 10)),   // relación externa
            (Cartera: 20, Interna: false, M: M(50, 50, 50, 50)),    // líder BASE GENERAL
        };

        var internas = pares.Where(p => p.Interna).GroupBy(p => p.Cartera)
            .ToDictionary(g => g.Key, g => MetricasLocalizacion.Sumar(g.Select(p => p.M)));
        var todas = pares.GroupBy(p => p.Cartera)
            .ToDictionary(g => g.Key, g => MetricasLocalizacion.Sumar(g.Select(p => p.M)));

        Assert.Equal([22], internas.Keys);
        Assert.Equal(50m, CalculoLocalizacion.PctContacto(internas[22]));
        Assert.Equal(30m, CalculoLocalizacion.PctContacto(todas[22]));
        Assert.Equal(2, todas.Count);
    }
}

/// <summary>Regla de corte de las 13:00 del coordinador del precálculo (copiada de CalificadorCarteraCache).</summary>
public class ReglaCorteLocalizacionTests
{
    private static readonly DateTime Hoy = new(2026, 9, 25);

    [Theory]
    [InlineData(12, 59, 24)]   // antes de las 13:00 → el corte vigente es el de ayer a las 13:00
    [InlineData(13, 0, 25)]    // a las 13:00 en punto ya cuenta el de hoy
    [InlineData(18, 30, 25)]
    [InlineData(0, 5, 24)]
    public void Ultimo_corte_programado(int hora, int minuto, int dia)
    {
        var ahora = Hoy.AddHours(hora).AddMinutes(minuto);
        Assert.Equal(new DateTime(2026, 9, dia, 13, 0, 0), ReglaCorteLocalizacion.UltimoCorteProgramado(ahora));
    }

    [Fact]
    public void Sin_corte_siempre_recalcula()
    {
        Assert.True(ReglaCorteLocalizacion.DebeRecalcular(null, Hoy.AddHours(9), alArrancar: false));
        Assert.True(ReglaCorteLocalizacion.DebeRecalcular(null, Hoy.AddHours(9), alArrancar: true));
    }

    [Fact]
    public void Primer_acceso_despues_de_las_13_recalcula_si_el_corte_es_de_la_manana()
    {
        var corte = Hoy.AddHours(9);   // 09:00 de hoy
        Assert.False(ReglaCorteLocalizacion.DebeRecalcular(corte, Hoy.AddHours(12).AddMinutes(59), alArrancar: false));
        Assert.True(ReglaCorteLocalizacion.DebeRecalcular(corte, Hoy.AddHours(13).AddMinutes(1), alArrancar: false));
    }

    [Fact]
    public void Corte_posterior_a_las_13_vale_hasta_las_13_del_dia_siguiente()
    {
        var corte = Hoy.AddHours(13).AddMinutes(5);
        Assert.False(ReglaCorteLocalizacion.DebeRecalcular(corte, Hoy.AddDays(1).AddHours(12), alArrancar: false));
        Assert.True(ReglaCorteLocalizacion.DebeRecalcular(corte, Hoy.AddDays(1).AddHours(13), alArrancar: false));
    }

    [Fact]
    public void Al_arrancar_recalcula_si_no_hay_corte_del_dia()
    {
        // Corte de ayer a las 15:00: vigente a las 08:00 para las lecturas, pero no es "del día".
        var corte = Hoy.AddDays(-1).AddHours(15);
        var ahora = Hoy.AddHours(8);
        Assert.False(ReglaCorteLocalizacion.DebeRecalcular(corte, ahora, alArrancar: false));
        Assert.True(ReglaCorteLocalizacion.DebeRecalcular(corte, ahora, alArrancar: true));
        // Con un corte de hoy no recalcula al arrancar.
        Assert.False(ReglaCorteLocalizacion.DebeRecalcular(Hoy.AddHours(7), ahora, alArrancar: true));
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;
using Zentinela.Infrastructure.SQLServer.Services.Localizacion;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// Recálculo programado de las 13:05 (ADR-040, etapa 4): horario, decisión de cada intento, reintentos y zona
/// horaria, con un reloj falso (las esperas avanzan el reloj al instante) y dobles del coordinador y de la BD.
/// </summary>
public class RecalculoProgramadoTests
{
    private static readonly TimeSpan H1305 = new(13, 5, 0);

    // ════════════════════════════════════════════════════════════════════════
    //  Reglas puras
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2026-09-26 10:00", "2026-09-26 13:05")]   // antes: hoy
    [InlineData("2026-09-26 13:04:59", "2026-09-26 13:05")]
    [InlineData("2026-09-26 13:05", "2026-09-27 13:05")]   // en punto ya corrió (el bucle vuelve a calcular después de correr)
    [InlineData("2026-09-26 18:30", "2026-09-27 13:05")]   // después: mañana
    [InlineData("2026-12-31 23:59", "2027-01-01 13:05")]
    public void Proxima_ejecucion_es_hoy_si_no_ha_llegado_y_si_no_manana(string ahora, string esperada)
    {
        Assert.Equal(DateTime.Parse(esperada), ProgramaRecalculoLocalizacion.ProximaEjecucion(DateTime.Parse(ahora), H1305));
    }

    [Theory]
    [InlineData("13:05", true, 13, 5)]
    [InlineData("7:30", true, 7, 30)]
    [InlineData("00:00", true, 0, 0)]
    [InlineData("24:00", false, 13, 5)]
    [InlineData("13h05", false, 13, 5)]
    [InlineData("", false, 13, 5)]
    [InlineData(null, false, 13, 5)]
    public void Hora_configurada_HH_mm_o_la_de_defecto(string? texto, bool valida, int h, int m)
    {
        Assert.Equal(valida, ProgramaRecalculoLocalizacion.TryParseHora(texto, out var hora));
        Assert.Equal(new TimeSpan(h, m, 0), hora);
    }

    [Fact]
    public void Decision_sin_tablas_ya_hay_corte_etl_o_calcular()
    {
        var ahora = new DateTime(2026, 9, 26, 13, 5, 0);
        Assert.Equal(DecisionCorteProgramado.SinTablas,
            ProgramaRecalculoLocalizacion.Decidir(new EstadoCorteProgramado(false, null, false), ahora));
        // Un corte de las 13:01 de hoy (primer acceso o un cargue) ya cubre: no se duplica.
        Assert.Equal(DecisionCorteProgramado.YaHayCorte,
            ProgramaRecalculoLocalizacion.Decidir(new EstadoCorteProgramado(true, ahora.Date.AddHours(13).AddMinutes(1), true), ahora));
        // Uno de las 12:59 no: la carga nocturna de las fuentes pudo no haber terminado.
        Assert.Equal(DecisionCorteProgramado.Calcular,
            ProgramaRecalculoLocalizacion.Decidir(new EstadoCorteProgramado(true, ahora.Date.AddHours(12).AddMinutes(59), false), ahora));
        Assert.Equal(DecisionCorteProgramado.EsperarEtl,
            ProgramaRecalculoLocalizacion.Decidir(new EstadoCorteProgramado(true, ahora.AddDays(-1), true), ahora));
        Assert.Equal(DecisionCorteProgramado.Calcular,
            ProgramaRecalculoLocalizacion.Decidir(new EstadoCorteProgramado(true, null, false), ahora));
    }

    [Fact]
    public void Ahora_es_la_hora_de_Colombia_aunque_el_equipo_este_en_otra_zona()
    {
        // 2026-09-26 12:50 UTC = 07:50 en Bogotá (UTC-5, sin horario de verano) = 14:50 en Madrid.
        var reloj = new TimeProviderFijo(new DateTimeOffset(2026, 9, 26, 12, 50, 0, TimeSpan.Zero));
        var colombia = ZonaHorariaLocalizacion.Resolver("America/Bogota");
        Assert.Equal(new DateTime(2026, 9, 26, 7, 50, 0), ZonaHorariaLocalizacion.Ahora(reloj, colombia));
        // El id de Windows resuelve a la misma zona.
        Assert.Equal(new DateTime(2026, 9, 26, 7, 50, 0),
            ZonaHorariaLocalizacion.Ahora(reloj, ZonaHorariaLocalizacion.Resolver("SA Pacific Standard Time")));
        // Una zona que no existe cae a la local (no lanza).
        Assert.Equal(TimeZoneInfo.Local, ZonaHorariaLocalizacion.Resolver("Marte/Olympus"));

        var r = new RelojLocalizacion(Options.Create(new InformesLocalizacionSettings()), reloj);
        Assert.Equal(new DateTime(2026, 9, 26, 7, 50, 0), r.Ahora);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Un día de intentos
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Con_el_ETL_en_curso_espera_y_reintenta_hasta_poder_calcular()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(
            new EstadoCorteProgramado(true, new DateTime(2026, 9, 25, 13, 5, 0), true),
            new EstadoCorteProgramado(true, new DateTime(2026, 9, 25, 13, 5, 0), true),
            new EstadoCorteProgramado(true, new DateTime(2026, 9, 25, 13, 5, 0), false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);

        var intentos = await Servicio(coord, pasos, reloj).EjecutarDiaAsync(default);

        Assert.Equal([DecisionCorteProgramado.EsperarEtl, DecisionCorteProgramado.EsperarEtl, DecisionCorteProgramado.Calcular],
                     intentos.Select(i => i.Decision));
        Assert.Equal(1, coord.Llamadas);
        Assert.Equal("CORTE_13H", coord.Motivos.Single());      // motivo que admite el CHECK de LocalizacionCorte
        Assert.Equal([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)], reloj.Esperas);
        Assert.Equal(1, pasos.Vinculaciones);                    // puesta al día del vínculo de retorno
    }

    [Fact]
    public async Task Un_corte_fallido_se_reintenta_y_otro_proceso_calculando_tambien()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(new EstadoCorteProgramado(true, null, false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Error, ResultadoRondaCorte.Omitido, ResultadoRondaCorte.Ok);

        var intentos = await Servicio(coord, pasos, reloj).EjecutarDiaAsync(default);

        Assert.Equal([ResultadoRondaCorte.Error, ResultadoRondaCorte.Omitido, ResultadoRondaCorte.Ok], intentos.Select(i => i.Resultado));
        Assert.Equal(3, coord.Llamadas);
        Assert.Equal(2, reloj.Esperas.Count);
    }

    [Fact]
    public async Task Sin_tablas_avisa_y_no_insiste_hasta_manana()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(new EstadoCorteProgramado(false, null, false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);

        var intentos = await Servicio(coord, pasos, reloj).EjecutarDiaAsync(default);

        Assert.Single(intentos);
        Assert.Equal(DecisionCorteProgramado.SinTablas, intentos[0].Decision);
        Assert.Equal(0, coord.Llamadas);
        Assert.Empty(reloj.Esperas);
        Assert.Equal(0, pasos.Vinculaciones);
    }

    [Fact]
    public async Task Si_ya_hay_corte_despues_de_las_13_no_calcula_otro()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(new EstadoCorteProgramado(true, new DateTime(2026, 9, 26, 13, 2, 0), false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);

        var intentos = await Servicio(coord, pasos, reloj).EjecutarDiaAsync(default);

        Assert.Equal(DecisionCorteProgramado.YaHayCorte, Assert.Single(intentos).Decision);
        Assert.Equal(0, coord.Llamadas);
        Assert.Equal(1, pasos.Vinculaciones);
    }

    [Fact]
    public async Task Agotados_los_reintentos_se_rinde_hasta_manana()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(new EstadoCorteProgramado(true, null, false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Error);

        var intentos = await Servicio(coord, pasos, reloj, maxReintentos: 3).EjecutarDiaAsync(default);

        Assert.Equal(4, intentos.Count);          // el intento inicial + 3 reintentos
        Assert.Equal(4, coord.Llamadas);
        Assert.Equal(3, reloj.Esperas.Count);
    }

    [Fact]
    public async Task Si_la_BD_no_responde_se_trata_como_fallo_y_se_reintenta()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 13, 5, 0));
        var pasos = new PasosFalsos(null, new EstadoCorteProgramado(true, null, false));   // null = lanza
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);

        var intentos = await Servicio(coord, pasos, reloj).EjecutarDiaAsync(default);

        Assert.Equal([ResultadoRondaCorte.Error, ResultadoRondaCorte.Ok], intentos.Select(i => i.Resultado));
        Assert.Equal(1, coord.Llamadas);
    }

    [Fact]
    public async Task El_bucle_espera_hasta_las_1305_y_despues_hasta_las_1305_del_dia_siguiente()
    {
        using var cts = new CancellationTokenSource();
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 10, 0, 0)) { CancelarEnEspera = 3, Cancelar = cts };
        var pasos = new PasosFalsos(new EstadoCorteProgramado(true, null, false));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);   // el corte "tarda" un minuto

        await Servicio(coord, pasos, reloj).BucleAsync(cts.Token);

        Assert.Equal(new TimeSpan(3, 5, 0), reloj.Esperas[0]);                               // 10:00 → 13:05
        Assert.Equal(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1), reloj.Esperas[1]);     // 13:06 → 13:05 de mañana
        Assert.Equal(2, coord.Llamadas);                                                    // un corte por día
        Assert.Equal(new DateTime(2026, 9, 27, 13, 5, 0), coord.Horas[1]);
    }

    [Fact]
    public async Task Desactivado_no_hace_nada()
    {
        var reloj = new RelojFalso(new DateTime(2026, 9, 26, 10, 0, 0));
        var coord = new CoordinadorFalso(reloj, ResultadoRondaCorte.Ok);
        var s = new LocalizacionRecalculoProgramado(coord, new PasosFalsos(), reloj,
            new RecalculoProgramadoLocalizacionSettings { Activado = false }, NullLogger<LocalizacionRecalculoProgramado>.Instance);

        await s.StartAsync(default);
        await (s.ExecuteTask ?? Task.CompletedTask);
        await s.StopAsync(default);

        Assert.Empty(reloj.Esperas);
        Assert.Equal(0, coord.Llamadas);
    }

    // ── Dobles ────────────────────────────────────────────────────────────────

    private static LocalizacionRecalculoProgramado Servicio(
        CoordinadorFalso coord, PasosFalsos pasos, RelojFalso reloj, int maxReintentos = 12) =>
        new(coord, pasos, reloj,
            new RecalculoProgramadoLocalizacionSettings { Hora = "13:05", MinutosEntreReintentos = 10, MaxReintentos = maxReintentos },
            NullLogger<LocalizacionRecalculoProgramado>.Instance);

    private sealed class TimeProviderFijo(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }

    /// <summary>Reloj que "espera" avanzando al instante; puede cancelar en la N-ésima espera.</summary>
    private sealed class RelojFalso(DateTime inicio) : IRelojLocalizacion
    {
        public DateTime Ahora { get; set; } = inicio;
        public List<TimeSpan> Esperas { get; } = [];
        public int CancelarEnEspera { get; init; } = int.MaxValue;
        public CancellationTokenSource? Cancelar { get; init; }

        public Task EsperarAsync(TimeSpan espera, CancellationToken ct)
        {
            if (Esperas.Count + 1 >= CancelarEnEspera && Cancelar is not null)
            {
                Cancelar.Cancel();
                throw new OperationCanceledException(ct);
            }
            Esperas.Add(espera);
            Ahora += espera;
            return Task.CompletedTask;
        }
    }

    /// <summary>Devuelve los estados en orden (el último se repite); null = la BD falla.</summary>
    private sealed class PasosFalsos(params EstadoCorteProgramado?[] estados) : IPasosRecalculoProgramado
    {
        private int _i;
        public int Vinculaciones { get; private set; }

        public Task<EstadoCorteProgramado> LeerEstadoAsync(CancellationToken ct)
        {
            var e = estados[Math.Min(_i++, estados.Length - 1)];
            return e is null ? throw new InvalidOperationException("BD caída") : Task.FromResult(e);
        }

        public Task VincularPendientesAsync(CancellationToken ct)
        {
            Vinculaciones++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Coordinador que devuelve resultados en orden (el último se repite) y tarda un minuto por corte.</summary>
    private sealed class CoordinadorFalso(RelojFalso reloj, params ResultadoRondaCorte[] resultados) : ILocalizacionResumenCoordinador
    {
        public int Llamadas { get; private set; }
        public List<string> Motivos { get; } = [];
        public List<DateTime> Horas { get; } = [];

        public Task<ResultadoRondaCorte> RecalcularYEsperarAsync(string motivo, CancellationToken ct)
        {
            Motivos.Add(motivo);
            Horas.Add(reloj.Ahora);
            var r = resultados[Math.Min(Llamadas++, resultados.Length - 1)];
            reloj.Ahora = reloj.Ahora.AddMinutes(1);
            return Task.FromResult(r);
        }

        public void SolicitarRecalculo(string motivo, string? usuario = null) => throw new NotSupportedException();
        public void VerificarVigencia(DateTime? ultimoCorteOk) => throw new NotSupportedException();
        public void VerificarAlArrancar() => throw new NotSupportedException();
        public bool Calculando => false;
        public string? MotivoEnCurso => null;
        public DateTime? InicioEnCurso => null;
        public long Version => 0;
    }
}

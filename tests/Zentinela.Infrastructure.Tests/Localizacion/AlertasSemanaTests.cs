using Zentinela.Application;
using Zentinela.Application.Settings;
using Zentinela.Application.Utils;

namespace Zentinela.Infrastructure.Tests.Localizacion;

/// <summary>
/// "Alertas de la semana" (ADR-040, etapa 4): cada regla con su umbral (estricto: "más de"), y el orden del panel.
/// Solo se muestran en la app: aquí no hay nada que se envíe.
/// </summary>
public class AlertasSemanaTests
{
    private static readonly AlertasSemanaLocalizacionSettings U = new();   // 3 pts, 15 d, 30 d, 40 %, 5.000, 30 d
    private static readonly DateTime Hoy = new(2026, 9, 26);

    private static MetricasLocalizacion M(int total, int conSaldo, int sinContactoSaldo) =>
        new(total, 0, 0, 0, 0, 0, conSaldo, sinContactoSaldo, sinContactoSaldo * 10_000m, conSaldo * 10_000m);

    [Fact]
    public void Caida_del_indice_mas_de_N_puntos_a_un_decimal()
    {
        IndiceSemanalLider[] lideres =
        [
            new(1, "Ana",   111, 60.0m, 65.0m),    // cayó 5 → alerta
            new(2, "Beto",  222, 62.0m, 65.0m),    // cayó 3 → NO (más de 3)
            new(3, "Carla", 333, 61.96m, 65.04m),  // se ve 62,0 vs 65,0 → 3,0 → NO
            new(4, "Dora",  444, 70m, 60m),        // subió
            new(5, "Eva",   555, 50m, null),       // sin semana anterior
        ];
        var a = Assert.Single(AlertasSemanaLocalizacion.CaidasDeIndice(lideres, U));
        Assert.Equal(AlertasSemanaLocalizacion.CaidaIndice, a.Tipo);
        Assert.Equal("alta", a.Severidad);
        Assert.Equal(1, a.LiderID);
        Assert.Equal(111, a.DocumentoLider);
        Assert.Equal(5.0m, a.Magnitud);
        Assert.Contains("bajó 5,0 puntos", a.Mensaje);
    }

    [Fact]
    public void Lote_enviado_hace_mas_de_15_dias_sin_retorno_vinculado()
    {
        LoteEnviadoAlerta L(int id, int diasAtras, long? cargue) =>
            new(id, $"LOC-{id:000000}", 3, "Líder 3", 333, 4, "BERTAL", "Fosyga", Hoy.AddDays(-diasAtras).AddHours(10), cargue, 200);

        var alertas = AlertasSemanaLocalizacion.LotesSinRetorno([L(1, 20, null), L(2, 15, null), L(3, 40, 509), L(4, 16, null)], Hoy, U).ToList();

        Assert.Equal([1, 4], alertas.Select(a => a.EnvioID!.Value));    // 15 días justos no; con vínculo no
        Assert.All(alertas, a => Assert.Equal("Fosyga", a.Fuente));
        Assert.Contains("LOC-000001", alertas[0].Mensaje);
        Assert.Contains("hace 20 días", alertas[0].Mensaje);
    }

    [Fact]
    public void Fuente_Funcional_sin_datos_nuevos_en_mas_de_30_dias()
    {
        FuenteCargueAlerta[] fuentes =
        [
            new("Fosyga", "Fosyga", EstadoFuente.Funcional, Hoy.AddDays(-2), Hoy.AddDays(-2)),
            new("Sura", "Sura", EstadoFuente.Funcional, Hoy.AddDays(-31), Hoy.AddDays(-31)),     // alerta: último cargue
            new("Tigo", "Tigo", EstadoFuente.Funcional, Hoy.AddDays(-30), null),                 // 30 justos: no
            // Legacy (sin cargues del módulo): cuenta el dato más reciente de la base.
            new("Coosalud", "Coosalud", EstadoFuente.Funcional, null, Hoy.AddDays(-3)),           // no
            new("RUES", "RUES", EstadoFuente.Funcional, null, Hoy.AddDays(-90)),                  // alerta: dato viejo
            new("SOS", "SOS", EstadoFuente.Funcional, Hoy.AddDays(-60), Hoy.AddDays(-10)),        // el dato nuevo manda: no
            new("MiVacuna", "MiVacuna", EstadoFuente.Funcional, null, null),                      // alerta: nada
            new("Runt", "Runt", EstadoFuente.Inactivo, null, null),                               // no Funcional
            new("Tigo2", "Tigo 2", EstadoFuente.Intermitente, Hoy.AddDays(-400), null),
        ];
        var alertas = AlertasSemanaLocalizacion.FuentesSinCargue(fuentes, Hoy, U).ToList();
        Assert.Equal(["Sura", "RUES", "MiVacuna"], alertas.Select(a => a.Fuente));
        Assert.Contains("31 días sin datos nuevos (último cargue el 26/08/2026)", alertas[0].Mensaje);
        Assert.Contains("90 días sin datos nuevos (dato más reciente del 28/06/2026, sin cargues registrados", alertas[1].Mensaje);
        Assert.Contains("no tiene cargues registrados ni datos", alertas[2].Mensaje);
    }

    [Fact]
    public void Cartera_con_mas_de_40_por_ciento_sin_contacto_con_saldo()
    {
        CarteraAlerta[] carteras =
        [
            new(1, "SG+", null, null, M(1000, 800, 450)),         // 45 % → alerta
            new(2, "BERTAL", null, null, M(1000, 800, 400)),      // 40 % justo → no
            new(3, "DELMAR", null, null, M(1000, 0, 0)),          // sin saldo en UET → null, sin alerta
            new(4, "ALIARCOM", 15, "Líder 15", M(100, 90, 60)),   // la parte de un líder: 60 %
        ];
        var alertas = AlertasSemanaLocalizacion.CarterasSinContacto(carteras, U).ToList();
        Assert.Equal([1, 4], alertas.Select(a => a.CarteraID!.Value));
        Assert.Contains("ALIARCOM — Líder 15: 60 %", alertas[1].Mensaje);
    }

    [Fact]
    public void Lider_con_cola_grande_y_sin_lotes_recientes()
    {
        ColaLiderAlerta[] colas =
        [
            new(1, "Ana", 1, 12_000, 1_500, 0),    // alerta
            new(2, "Beto", 2, 12_000, 1_500, 1),   // envió
            new(3, "Carla", 3, 5_000, 800, 0),     // cola justa en el umbral: no
            new(4, "Dora", 4, 0, 0, 0),
        ];
        var a = Assert.Single(AlertasSemanaLocalizacion.ColasSinLotes(colas, U));
        Assert.Equal(1, a.LiderID);
        Assert.Contains("12.000 búsquedas sugeridas", a.Mensaje);
        Assert.Contains("1.500 titulares nunca buscados", a.Mensaje);
        Assert.Contains("últimos 30 días", a.Mensaje);
    }

    [Fact]
    public void Orden_alta_primero_por_tipo_y_magnitud()
    {
        var alertas = AlertasSemanaLocalizacion.Ordenar(
        [
            new() { Tipo = AlertasSemanaLocalizacion.FuenteSinCargue, Severidad = "media", Magnitud = 50, Mensaje = "f" },
            new() { Tipo = AlertasSemanaLocalizacion.CarteraSinContacto, Severidad = "alta", Magnitud = 45, Mensaje = "c45" },
            new() { Tipo = AlertasSemanaLocalizacion.CaidaIndice, Severidad = "alta", Magnitud = 4, Mensaje = "i4" },
            new() { Tipo = AlertasSemanaLocalizacion.CaidaIndice, Severidad = "alta", Magnitud = 9, Mensaje = "i9" },
            new() { Tipo = AlertasSemanaLocalizacion.LoteSinRetorno, Severidad = "media", Magnitud = 20, Mensaje = "l" },
        ]);
        Assert.Equal(["i9", "i4", "c45", "l", "f"], alertas.Select(a => a.Mensaje));
    }
}

using Zentinela.Application;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes.Reglas;

/// <summary>
/// Regla por fuente. Separado de <see cref="CatalogoCargueFuentes"/> porque las reglas son
/// código con lógica (y a veces SQL) y viven en Infrastructure; la definición la consume el front.
/// </summary>
internal static class CatalogoReglasFuente
{
    private static readonly Dictionary<string, IReglaFuente> Reglas = new(StringComparer.OrdinalIgnoreCase)
    {
        [CatalogoCargueFuentes.Sura.Fuente]      = new ReglaGenerica(CatalogoCargueFuentes.Sura.Fuente),
        [CatalogoCargueFuentes.Cajacopi.Fuente]  = new ReglaCajacopi(),
        [CatalogoCargueFuentes.MutualSer.Fuente] = new ReglaMutualSer(),
        // NuevaEps: sp_ProcesarNuevaEpsAuxiliar solo creaba Titular/relaciones, calculaba el flag
        // e insertaba: todo genérico. Lo propio (TitularID varchar) es de la definición.
        [CatalogoCargueFuentes.NuevaEps.Fuente]  = new ReglaGenerica(CatalogoCargueFuentes.NuevaEps.Fuente),
        [CatalogoCargueFuentes.Simit.Fuente]     = new ReglaSimit(),
        [CatalogoCargueFuentes.DatosClaro.Fuente] = new ReglaDatosClaro(),

        // ── Fase 2, grupo salud ──
        [CatalogoCargueFuentes.Positiva.Fuente]   = new ReglaPositiva(),
        // SaludTotal: sp_ProcesarSaludTotalAuxiliar solo pasaba vacíos a NULL ('' , NIT 0, fecha
        // 1900-01-01), creaba Titular/relaciones, calculaba el flag e insertaba: todo genérico.
        // No cruzaba ListaNegraCorreos ni validaba el teléfono: el motor sí (ADR-039 §4).
        [CatalogoCargueFuentes.SaludTotal.Fuente] = new ReglaGenerica(CatalogoCargueFuentes.SaludTotal.Fuente),
    };

    public static bool Existe(string fuente) => Reglas.ContainsKey(fuente);

    public static IReglaFuente Obtener(string fuente) =>
        Reglas.TryGetValue(fuente, out var regla)
            ? regla
            : throw new ArgumentException($"La fuente '{fuente}' no tiene regla de cargue registrada.");
}

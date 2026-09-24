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
        // ── Fase 2 · grupo "otras" ──
        [CatalogoCargueFuentes.Fosyga.Fuente]    = new ReglaFosyga(),
        [CatalogoCargueFuentes.Rgm.Fuente]       = new ReglaRgm(),
        // TigoUne: sp_ProcesarTigoUneAuxiliar solo hacía lo genérico (Titular, relaciones, dedup
        // por titular, lista negra de correos —con LIKE, no se copia—, teléfono de 10, flag,
        // INSERT de lo nuevo). Ver el <summary> de CatalogoCargueFuentes.TigoUne.
        [CatalogoCargueFuentes.TigoUne.Fuente]   = new ReglaGenerica(CatalogoCargueFuentes.TigoUne.Fuente),
        // Tigo: NO hay SP que traducir (la tabla se llenó a mano en 2025 y nadie la escribe desde
        // entonces). Misma forma que TigoUne → lo genérico basta. Ver CatalogoCargueFuentes.Tigo.
        [CatalogoCargueFuentes.Tigo.Fuente]      = new ReglaGenerica(CatalogoCargueFuentes.Tigo.Fuente),
    };

    public static bool Existe(string fuente) => Reglas.ContainsKey(fuente);

    public static IReglaFuente Obtener(string fuente) =>
        Reglas.TryGetValue(fuente, out var regla)
            ? regla
            : throw new ArgumentException($"La fuente '{fuente}' no tiene regla de cargue registrada.");
}

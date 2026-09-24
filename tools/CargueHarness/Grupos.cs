namespace CargueHarness;

/// <summary>
/// Un caso del harness: la fuente (nombre para <c>--fuente</c>), la tabla destino cuyo
/// COUNT(*) debe quedar igual al terminar, y el método que lo corre.
/// </summary>
internal sealed record CasoHarness(string Fuente, string TablaDestino, Func<Harness, Task> Correr);

/// <summary>
/// Un grupo de fuentes con su rango RESERVADO de TitularIDs y su usuario de auditoría. Cada
/// grupo corre en su propio worktree y en paralelo con los demás, así que la verificación de
/// rango libre, la limpieza y el borrado de ejecuciones/reversiones solo tocan lo suyo.
/// </summary>
internal sealed record GrupoHarness(string Nombre, long Desde, long Hasta, string Usuario, IReadOnlyList<CasoHarness> Casos)
{
    /// <summary>
    /// Base del sub-rango de 100 número <paramref name="bloque"/> (0..9) dentro del grupo, para
    /// los casos nuevos: <c>T(n) = h.Grupo.Bloque(k) + n</c> con n en 1..99. Ej.: en salud,
    /// Bloque(0) = 999000001000 y Bloque(3) = 999000001300. (Los casos de fase1 conservan sus
    /// bases fijas, que coinciden con Bloque(0..5) de fase1.)
    /// </summary>
    public long Bloque(int bloque)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bloque);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bloque, 9);
        return Desde / 1000 * 1000 + 100L * bloque;
    }
}

/// <summary>
/// Registro central de grupos. Cada equipo agrega SUS casos SOLO en la lista de SU grupo (una
/// línea por caso) para no chocar en los merges con los demás. Los rangos no se solapan: no
/// cambiarlos sin revisar que ningún otro grupo quede encima.
/// </summary>
internal static class Grupos
{
    public const string PorDefecto = "fase1";

    // ── fase1: 999000000001..999000000999 — sub-rangos fijos de 100 por fuente ──
    public static readonly GrupoHarness Fase1 = new("fase1", 999000000001, 999000000999, "harness-cargue",
    [
        new("Sura",       "Sura",       CasoSura.Correr),         // 001-099
        new("Cajacopi",   "Cajacopi",   CasoCajacopi.Correr),     // 100-199
        new("MutualSer",  "MutualSer",  CasoMutualSer.Correr),    // 200-299
        new("NuevaEps",   "NuevaEps",   CasoNuevaEps.Correr),     // 300-399
        new("SIMIT",      "SIMIT",      CasoSimit.Correr),        // 400-499
        new("DatosClaro", "DatosClaro", CasoDatosClaro.Correr),   // 500-599
    ]);

    // ── salud: 999000001000..999000001999 ──
    public static readonly GrupoHarness Salud = new("salud", 999000001000, 999000001999, "harness-cargue-salud",
    [
        new("Positiva",   "Positiva",   CasoPositiva.Correr),     // Bloque(0): 1000-1099
        new("SaludTotal", "SaludTotal", CasoSaludTotal.Correr),   // Bloque(1): 1100-1199
        new("SOS",        "SOS",        CasoSos.Correr),          // Bloque(2): 1200-1299
        new("Coosalud",   "Coosalud",   CasoCoosalud.Correr),     // Bloque(3): 1300-1399
        new("SaviaSalud", "SaviaSalud", CasoSaviaSalud.Correr),   // Bloque(4): 1400-1499
        new("MiVacuna",   "MiVacuna",   CasoMiVacuna.Correr),     // Bloque(5): 1500-1599
    ]);

    // ── empresas: 999000002000..999000002999 ──
    public static readonly GrupoHarness Empresas = new("empresas", 999000002000, 999000002999, "harness-cargue-empresas",
    [
        new("RUES",             "RUES",             CasoRues.Correr),              // Bloque(0): 2000-2099
        new("Camara",           "Camara",           CasoCamara.Correr),            // Bloque(1): 2100-2199
        new("CamaraEmpresa",    "CamaraEmpresa",    CasoCamaraEmpresa.Correr),     // Bloque(2): 2200-2299 (NitEmpresa)
        new("PositivaEmpresas", "PositivaEmpresas", CasoPositivaEmpresas.Correr),  // Bloque(3): 2300-2399
        new("ARLSura",          "ARLSura",          CasoArlSura.Correr),           // Bloque(4): 2400-2499 (≠ Sura, la EPS)
        new("Compensados",      "Compensados",      CasoCompensados.Correr),       // Bloque(5): 2500-2599
    ]);

    // ── otras: 999000003000..999000003999 ──
    public static readonly GrupoHarness Otras = new("otras", 999000003000, 999000003999, "harness-cargue-otras",
    [
        new("Fosyga",     "Fosyga",     CasoFosyga.Correr),       // Bloque(0): 3000-3099
        new("RGM",        "RGM",        CasoRgm.Correr),          // Bloque(1): 3100-3199
        new("TigoUne",    "TigoUne",    CasoTigoUne.Correr),      // Bloque(2): 3200-3299
        new("Tigo",       "Tigo",       CasoTigo.Correr),         // Bloque(3): 3300-3399 (sin SP legacy)
    ]);

    // ── desglose: 999000004000..999000004999 ──
    public static readonly GrupoHarness Desglose = new("desglose", 999000004000, 999000004999, "harness-cargue-desglose",
    [
    ]);

    public static readonly IReadOnlyList<GrupoHarness> Todos = [Fase1, Salud, Empresas, Otras, Desglose];

    public static GrupoHarness? Buscar(string nombre) =>
        Todos.SingleOrDefault(g => string.Equals(g.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
}

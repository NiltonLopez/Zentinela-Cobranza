namespace Zentinela.Application.Settings;

/// <summary>
/// Configuración de los Informes de Localización (ADR-040) — sección "InformesLocalizacion" de
/// appsettings.json. Todo tiene un valor por defecto: la sección puede faltar.
/// </summary>
public class InformesLocalizacionSettings
{
    // ── Índice de localización (0–100) ───────────────────────────────────────
    // Índice = 100 × (PesoContacto × %contacto + PesoCobertura × %cobertura funcional
    //                 + PesoFrescura × %frescura) / (suma de los pesos).
    // Se divide por la suma para que un ajuste que no sume 1 no saque el índice de 0–100.

    public decimal PesoContacto { get; set; } = 0.50m;
    public decimal PesoCobertura { get; set; } = 0.25m;
    public decimal PesoFrescura { get; set; } = 0.25m;

    // ── Alertas del Resumen ──────────────────────────────────────────────────

    /// <summary>Fuente Funcional con más de este % de titulares sin buscar.</summary>
    public decimal AlertaPorBuscarFuncionalPct { get; set; } = 30;

    /// <summary>Fuente cuyo último dato (en el alcance) tiene más de estos días.</summary>
    public int AlertaDiasSinDato { get; set; } = 365;

    /// <summary>Par con más de este % de sus titulares sin contacto y con saldo.</summary>
    public decimal AlertaSinContactoConSaldoPct { get; set; } = 40;

    // ── Rankings ─────────────────────────────────────────────────────────────

    /// <summary>Filas del ranking de líderes que ve un Líder (además de la suya).</summary>
    public int TopRankingLider { get; set; } = 5;

    // ── Precálculo ───────────────────────────────────────────────────────────

    /// <summary>Vida de la caché de lectura del corte (se renueva sola con cada corte nuevo).</summary>
    public int MinutosCacheLectura { get; set; } = 10;

    /// <summary>Tras un corte fallido, los disparos automáticos esperan esto antes de reintentar.</summary>
    public int MinutosReintentoTrasError { get; set; } = 15;

    /// <summary>Calcular un corte al arrancar la API si no hay uno OK del día.</summary>
    public bool CalcularAlArrancar { get; set; } = true;

    /// <summary>Espera antes del cálculo de arranque, para no competir con el calentamiento de la API.</summary>
    public int SegundosEsperaArranque { get; set; } = 30;
}

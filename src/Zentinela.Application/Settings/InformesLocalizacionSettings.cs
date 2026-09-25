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

    // ── Sugerencias de localización (etapa 2) ────────────────────────────────

    /// <summary>Pesos del puntaje y límites de las sugerencias (subsección "Sugerencias").</summary>
    public SugerenciasLocalizacionSettings Sugerencias { get; set; } = new();

    // ── Envíos a localización (etapa 3) ──────────────────────────────────────

    /// <summary>Límites de los lotes y ventanas del uso del departamento (subsección "Envios").</summary>
    public EnviosLocalizacionSettings Envios { get; set; } = new();
}

/// <summary>Envíos a localización (ADR-040, etapa 3).</summary>
public class EnviosLocalizacionSettings
{
    /// <summary>Tope de titulares por lote (el mismo del top de sugerencias).</summary>
    public int MaxTitularesLote { get; set; } = 5000;

    /// <summary>Lotes que devuelve la lista, los más recientes primero.</summary>
    public int MaxLotesLista { get; set; } = 300;

    /// <summary>Filas por página del detalle de un lote (tope).</summary>
    public int MaxTamPaginaDetalle { get; set; } = 500;

    /// <summary>Los rankings de Uso y Retorno miran los lotes enviados en estos últimos días.</summary>
    public int DiasVentanaRanking { get; set; } = 90;
}

/// <summary>
/// Puntaje explicable de las sugerencias de localización (ADR-040, etapa 2). Cada punto se suma por
/// separado y el desglose viaja al front: cambiar un peso aquí cambia el orden y la explicación a la vez.
/// </summary>
public class SugerenciasLocalizacionSettings
{
    /// <summary>UltimoPerfil ilocalizado, número equivocado / errado / fuera de servicio o "datos de localización".</summary>
    public int PuntosPerfilSinDatos { get; set; } = 40;

    /// <summary>Otro perfil de marcación sin contacto, "SIN GESTION" o sin perfil.</summary>
    public int PuntosPerfilSinContacto { get; set; } = 25;

    /// <summary>Máximo por saldo: PuntosSaldoMax × min(1, Saldo / P90 del saldo del par).</summary>
    public int PuntosSaldoMax { get; set; } = 30;

    /// <summary>Última gestión hace más de 90 días, o nunca.</summary>
    public int PuntosGestionMas90 { get; set; } = 15;

    /// <summary>Última gestión hace más de 30 días (y 90 o menos).</summary>
    public int PuntosGestionMas30 { get; set; } = 5;

    /// <summary>Nunca buscado en ninguna fuente Funcional.</summary>
    public int PuntosNuncaFuncional { get; set; } = 15;

    /// <summary>Nunca buscado en la fuente de la sugerencia.</summary>
    public int PuntosNuncaFuente { get; set; } = 10;

    /// <summary>Buscado en la fuente, no encontrado, y el dato ya es viejo.</summary>
    public int PuntosNoEncontradoViejo { get; set; } = 5;

    /// <summary>Se RESTA si ya tiene contacto fresco (celular o email en una Funcional de hace pocos días).</summary>
    public int PenalizacionContactoFresco { get; set; } = 100;

    /// <summary>Días que un contacto se considera fresco.</summary>
    public int DiasContactoFresco { get; set; } = 180;

    /// <summary>Tope de filas que devuelve una consulta (y el Excel).</summary>
    public int TopMaximo { get; set; } = 5000;

    /// <summary>Datos de gestión con más de estas horas: la respuesta trae un aviso.</summary>
    public int HorasDatosGestionViejos { get; set; } = 72;

    /// <summary>
    /// Una corrida del ETL 'EN CURSO' más vieja que esto se considera colgada: ya no bloquea (409), solo avisa.
    /// La corrida normal nunca tardó más de 46 min.
    /// </summary>
    public int MinutosEtlColgado { get; set; } = 180;
}

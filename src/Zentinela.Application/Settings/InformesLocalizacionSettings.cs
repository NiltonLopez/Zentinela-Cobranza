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

    // ── Etapa 4 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Zona horaria del negocio (Colombia). La hora de "ahora" de los cortes, la regla de las 13:00 y el
    /// recálculo programado se evalúan en esta zona y no con el reloj del equipo donde corre la API: el equipo
    /// de desarrollo está en otra zona que la BD (medido: 7 h de diferencia). Acepta el id IANA
    /// ("America/Bogota") o el de Windows ("SA Pacific Standard Time"); si no existe, se usa la zona local.
    /// </summary>
    public string ZonaHoraria { get; set; } = "America/Bogota";

    /// <summary>Recálculo diario en segundo plano (subsección "RecalculoProgramado").</summary>
    public RecalculoProgramadoLocalizacionSettings RecalculoProgramado { get; set; } = new();

    /// <summary>Tendencia semanal (subsección "Tendencia").</summary>
    public TendenciaLocalizacionSettings Tendencia { get; set; } = new();

    /// <summary>Ranking de asesores (subsección "Asesores").</summary>
    public AsesoresLocalizacionSettings Asesores { get; set; } = new();

    /// <summary>Vínculo automático lote ↔ cargue de retorno (subsección "VinculoRetorno").</summary>
    public VinculoRetornoLocalizacionSettings VinculoRetorno { get; set; } = new();

    /// <summary>Umbrales del panel "Alertas de la semana" (subsección "AlertasSemana").</summary>
    public AlertasSemanaLocalizacionSettings AlertasSemana { get; set; } = new();
}

/// <summary>
/// Recálculo programado del corte (ADR-040, etapa 4): un servicio en segundo plano pide un corte cada día a
/// <see cref="Hora"/> (hora de <see cref="InformesLocalizacionSettings.ZonaHoraria"/>). El cálculo perezoso de
/// las 13:00 (primer acceso) queda como respaldo.
/// </summary>
public class RecalculoProgramadoLocalizacionSettings
{
    public bool Activado { get; set; } = true;

    /// <summary>Hora del día ("HH:mm"), en la zona del negocio. 13:05: la carga nocturna de las fuentes ya terminó.</summary>
    public string Hora { get; set; } = "13:05";

    /// <summary>Espera entre reintentos (corte fallido, ETL de gestión en curso u otro proceso calculando).</summary>
    public int MinutosEntreReintentos { get; set; } = 10;

    /// <summary>Reintentos del día; agotados, se espera al día siguiente (el cálculo perezoso sigue de respaldo).</summary>
    public int MaxReintentos { get; set; } = 12;
}

/// <summary>Tendencia semanal (ADR-040, etapa 4).</summary>
public class TendenciaLocalizacionSettings
{
    public int SemanasPorDefecto { get; set; } = 12;

    /// <summary>Tope: la retención de cortes es de 26 semanas.</summary>
    public int SemanasMaximas { get; set; } = 26;
}

/// <summary>Ranking de asesores (ADR-040, etapa 4).</summary>
public class AsesoresLocalizacionSettings
{
    /// <summary>
    /// Un asesor con menos titulares que esto en el alcance no entra al ranking (se lista aparte): con 3
    /// titulares un 100 % no dice nada.
    /// </summary>
    public int MinTitulares { get; set; } = 30;

    /// <summary>Días de "gestionado recientemente".</summary>
    public int DiasGestionReciente { get; set; } = 30;
}

/// <summary>Vínculo automático lote ↔ cargue de retorno (ADR-040, etapa 4).</summary>
public class VinculoRetornoLocalizacionSettings
{
    /// <summary>
    /// Si el cargue que devolvió el lote trae al menos este % de sus titulares, el lote pasa solo a CERRADO.
    /// null (por defecto) = nunca se cierra solo: lo cierra una persona.
    /// </summary>
    public decimal? CerrarAlCubrirPct { get; set; }
}

/// <summary>Umbrales del panel "Alertas de la semana" (ADR-040, etapa 4). Solo se muestran en la app.</summary>
public class AlertasSemanaLocalizacionSettings
{
    /// <summary>Líder cuyo índice cayó más de estos puntos contra el último corte de la semana anterior.</summary>
    public decimal CaidaIndicePuntos { get; set; } = 3;

    /// <summary>Lote ENVIADO hace más de estos días sin cargue de retorno vinculado.</summary>
    public int DiasLoteSinRetorno { get; set; } = 15;

    /// <summary>Fuente Funcional sin ningún cargue OK del Cargue de Fuentes en estos días.</summary>
    public int DiasFuenteSinCargue { get; set; } = 30;

    /// <summary>Cartera con más de este % de titulares sin contacto y con saldo.</summary>
    public decimal SinContactoConSaldoPct { get; set; } = 40;

    /// <summary>Cola sugerida (aproximada, del corte) desde la que un líder sin lotes recientes genera alerta.</summary>
    public int ColaGrande { get; set; } = 5000;

    /// <summary>Ventana de "sin lotes enviados" del líder con cola grande.</summary>
    public int DiasSinLotes { get; set; } = 30;
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

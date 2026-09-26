namespace Zentinela.Application.DTOs.InformesLocalizacion;

// DTOs de la etapa 4 de /api/informes/localizacion (ADR-040): tendencia semanal, ranking de asesores y
// alertas de la semana. Mismas convenciones que el resto: porcentajes 0–100 con dos decimales, null sin
// universo.

// ── Tendencia ─────────────────────────────────────────────────────────────────

/// <summary>Una semana de la tendencia: el último corte OK de esa semana (lunes a domingo).</summary>
public class SemanaTendenciaDto
{
    /// <summary>Lunes de la semana.</summary>
    public DateTime InicioSemana { get; set; }
    public int CorteID { get; set; }
    public DateTime FechaCorte { get; set; }
    public MetricasLocalizacionDto Metricas { get; set; } = new();
}

/// <summary>Un punto de una fuente en una semana.</summary>
public class PuntoFuenteTendenciaDto
{
    public DateTime InicioSemana { get; set; }
    public int Total { get; set; }
    public int Buscados { get; set; }
    public int Encontrados { get; set; }
    /// <summary>Buscados / Total.</summary>
    public decimal? PctCobertura { get; set; }
    /// <summary>Encontrados / Total (la barra verde del Resumen).</summary>
    public decimal? PctLocalizacion { get; set; }
}

public class FuenteTendenciaDto
{
    public string Clave { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public EstadoFuente Estado { get; set; }
    public List<PuntoFuenteTendenciaDto> Puntos { get; set; } = [];
}

/// <summary><c>GET tendencia</c>.</summary>
public class TendenciaLocalizacionDto
{
    /// <summary>global | lider | cartera | par (el mismo universo del Resumen).</summary>
    public string Alcance { get; set; } = string.Empty;
    public string EtiquetaAlcance { get; set; } = string.Empty;
    public int SemanasPedidas { get; set; }
    /// <summary>Solo las semanas con algún corte OK, de la más vieja a la más reciente.</summary>
    public List<SemanaTendenciaDto> Semanas { get; set; } = [];
    public List<FuenteTendenciaDto> Fuentes { get; set; } = [];
    public PesosIndiceDto Pesos { get; set; } = new();
}

// ── Asesores ──────────────────────────────────────────────────────────────────

/// <summary>Un asesor en el ranking (su cédula es el documento de la foto).</summary>
public class FilaAsesorDto
{
    /// <summary>null = no entra al ranking (menos titulares que el mínimo).</summary>
    public int? Posicion { get; set; }
    /// <summary><c>UltimoEstadoTitular.AgenteID</c> = <c>Agente.AgenteID</c> = cédula.</summary>
    public long Documento { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal? Valor { get; set; }
    /// <summary>Contra el último corte OK de un día anterior (como los rankings de líderes).</summary>
    public decimal? Delta { get; set; }
    /// <summary>Titulares del alcance cuya última gestión hizo este asesor.</summary>
    public int Titulares { get; set; }
    /// <summary>Su última gestión fue con contacto (titular, tercero o acuerdo).</summary>
    public int ContactoEfectivo { get; set; }
    public decimal? PctContactoEfectivo { get; set; }
    /// <summary>Con última gestión en los últimos <see cref="AsesoresLocalizacionDto.DiasGestionReciente"/> días.</summary>
    public int GestionadosRecientes { get; set; }
    public decimal? PctGestionadosRecientes { get; set; }
    /// <summary>Con celular o email en alguna fuente de localización (misma regla del Resumen).</summary>
    public int ConDato { get; set; }
    public decimal? PctConDato { get; set; }
    public int ConSaldo { get; set; }
    public int SinContactoConSaldo { get; set; }
    public decimal? PctSinContactoConSaldo { get; set; }
    public decimal SaldoSinContacto { get; set; }
    /// <summary>En cuántas carteras del alcance tiene titulares.</summary>
    public int Carteras { get; set; }
}

/// <summary><c>GET asesores</c>.</summary>
public class AsesoresLocalizacionDto
{
    public CorteLocalizacionDto Corte { get; set; } = new();
    public CorteLocalizacionDto? CorteAnterior { get; set; }
    public string Alcance { get; set; } = string.Empty;
    public string EtiquetaAlcance { get; set; } = string.Empty;
    /// <summary>Contactabilidad | Gestion | ConDato | SinContactoSaldo.</summary>
    public string Criterio { get; set; } = string.Empty;
    public bool MenorEsMejor { get; set; }
    public int MinTitulares { get; set; }
    public int DiasGestionReciente { get; set; }
    /// <summary>Rankeados, por posición.</summary>
    public List<FilaAsesorDto> Asesores { get; set; } = [];
    /// <summary>Con menos de <see cref="MinTitulares"/> titulares: se listan sin posición.</summary>
    public List<FilaAsesorDto> PocosTitulares { get; set; } = [];
    /// <summary>Titulares del alcance sin asesor en su última gestión (sin gestión, marcador automático o usuario no registrado).</summary>
    public int TitularesSinAsesor { get; set; }
    public int TitularesAlcance { get; set; }
    public string? Aviso { get; set; }
}

// ── Alertas de la semana ──────────────────────────────────────────────────────

public class AlertaSemanaDto
{
    /// <summary>CAIDA_INDICE | LOTE_SIN_RETORNO | FUENTE_SIN_CARGUE | CARTERA_SIN_CONTACTO | COLA_SIN_LOTES.</summary>
    public string Tipo { get; set; } = string.Empty;
    /// <summary>alta | media.</summary>
    public string Severidad { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int? LiderID { get; set; }
    public string? NombreLider { get; set; }
    public long? DocumentoLider { get; set; }
    public int? CarteraID { get; set; }
    public int? EnvioID { get; set; }
    public string? Fuente { get; set; }
    /// <summary>Para ordenar dentro del tipo (puntos caídos, días, %, cola…).</summary>
    public decimal? Magnitud { get; set; }
}

/// <summary><c>GET alertas-semana</c>: se calcula al pedirla, no se envía a nadie.</summary>
public class AlertasSemanaDto
{
    /// <summary>Lunes de la semana actual.</summary>
    public DateTime Semana { get; set; }
    public CorteLocalizacionDto Corte { get; set; } = new();
    /// <summary>Último corte OK de la semana anterior (null: no hay; la regla de caída del índice no aplica).</summary>
    public CorteLocalizacionDto? CorteSemanaAnterior { get; set; }
    public List<AlertaSemanaDto> Alertas { get; set; } = [];
    /// <summary>Reglas que no se pudieron evaluar (p. ej. faltan las tablas de envíos).</summary>
    public List<string> Avisos { get; set; } = [];
    public UmbralesAlertasSemanaDto Umbrales { get; set; } = new();
}

public class UmbralesAlertasSemanaDto
{
    public decimal CaidaIndicePuntos { get; set; }
    public int DiasLoteSinRetorno { get; set; }
    public int DiasFuenteSinCargue { get; set; }
    public decimal SinContactoConSaldoPct { get; set; }
    public int ColaGrande { get; set; }
    public int DiasSinLotes { get; set; }
}

// ── Vínculo lote ↔ cargue de retorno ──────────────────────────────────────────

/// <summary>Un lote que quedó vinculado a un cargue de retorno.</summary>
public sealed record LoteVinculado(int EnvioID, long EjecucionID, DateTime FechaRetorno, int Titulares, int EnCargue, bool Cerrado);

/// <summary>Resultado de una pasada del vínculo automático (tras un cargue o la de puesta al día diaria).</summary>
public sealed record ResultadoVinculoRetorno(IReadOnlyList<LoteVinculado> Vinculados, bool TablasFaltantes)
{
    public static readonly ResultadoVinculoRetorno SinTablas = new([], true);
}

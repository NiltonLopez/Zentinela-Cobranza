namespace Zentinela.Application.DTOs.InformesLocalizacion;

// DTOs de /api/informes/localizacion (ADR-040). Los porcentajes van en escala 0–100 con dos decimales
// (null cuando no hay universo); los enums viajan como texto (JsonStringEnumConverter).

// ── Excepciones que el controlador traduce a HTTP ─────────────────────────────

/// <summary>
/// Las tablas del precálculo no existen todavía: el usuario debe aplicar el script. El controlador
/// responde 503 con este mensaje.
/// </summary>
public sealed class LocalizacionTablasFaltantesException()
    : Exception($"Falta aplicar {LocalizacionTablasFaltantesException.Script}: las tablas de los Informes de Localización todavía no existen en DBCallCenter.")
{
    public const string Script = "docs/sql/migrations/2026-09-26_informes_localizacion.sql";
}

/// <summary>Aún no hay ningún corte OK (se está calculando el primero). El controlador responde 409.</summary>
public sealed class LocalizacionSinCorteException(bool calculando)
    : Exception(calculando
        ? "Se está calculando el primer corte de localización. Vuelve a intentarlo en un par de minutos."
        : "Todavía no hay un corte de localización calculado.")
{
    public bool Calculando { get; } = calculando;
}

// ── Corte y estado ────────────────────────────────────────────────────────────

/// <summary>Un precálculo (<c>dbo.LocalizacionCorte</c>).</summary>
public class CorteLocalizacionDto
{
    public int CorteID { get; set; }
    /// <summary>Inicio del cálculo: "Localización al …".</summary>
    public DateTime FechaCorte { get; set; }
    /// <summary>ARRANQUE | CORTE_13H | CARGUE_FUENTE | MANUAL.</summary>
    public string Motivo { get; set; } = string.Empty;
    public string? Usuario { get; set; }
    /// <summary>EN CURSO | OK | ERROR.</summary>
    public string Estado { get; set; } = string.Empty;
    public int? DuracionMs { get; set; }
    public int? Pares { get; set; }
    public int? Titulares { get; set; }
    /// <summary>Última carga OK del ETL de gestión al calcular: "Gestión al …".</summary>
    public DateTime? DatosGestionAl { get; set; }
    public string? Error { get; set; }
}

/// <summary><c>GET resumen/estado</c>: qué corte se está mostrando y si hay uno calculándose.</summary>
public class EstadoLocalizacionDto
{
    /// <summary>Hay un cálculo en curso en esta API.</summary>
    public bool Calculando { get; set; }
    public string? MotivoEnCurso { get; set; }
    public DateTime? InicioEnCurso { get; set; }
    /// <summary>El corte que leen los informes (null = todavía no hay ninguno).</summary>
    public CorteLocalizacionDto? UltimoOk { get; set; }
    /// <summary>El último intento, OK o no (para ver errores).</summary>
    public CorteLocalizacionDto? UltimoIntento { get; set; }
    /// <summary>El último OK es posterior al corte de las 13:00 más reciente.</summary>
    public bool Vigente { get; set; }
}

// ── Piezas comunes ────────────────────────────────────────────────────────────

/// <summary>Métricas que cruzan fuentes para el alcance pedido.</summary>
public class MetricasLocalizacionDto
{
    public int Total { get; set; }
    /// <summary>Celular o email en la última fila de alguna fuente (= Total − SinContacto del Calificador).</summary>
    public int ConContacto { get; set; }
    public int SinContacto { get; set; }
    public int ConContactoFuncional { get; set; }
    public int BuscadosAlguna { get; set; }
    /// <summary>Buscados en al menos una fuente Funcional (cobertura funcional).</summary>
    public int BuscadosFuncional { get; set; }
    public int NuncaBuscadosFuncional { get; set; }
    /// <summary>Su consulta más reciente (cualquier fuente) tiene 180 días o menos.</summary>
    public int Frescos180 { get; set; }
    public int ConSaldo { get; set; }
    public int SinContactoConSaldo { get; set; }
    public decimal SaldoSinContacto { get; set; }
    public decimal SaldoTotal { get; set; }
    public decimal? PctContacto { get; set; }
    public decimal? PctCobertura { get; set; }
    public decimal? PctFrescura { get; set; }
    public decimal? PctSinContactoConSaldo { get; set; }
    /// <summary>Índice de localización 0–100 (pesos en <see cref="PesosIndiceDto"/>).</summary>
    public decimal? Indice { get; set; }
}

public class PesosIndiceDto
{
    public decimal Contacto { get; set; }
    public decimal Cobertura { get; set; }
    public decimal Frescura { get; set; }
}

/// <summary>Una fuente dentro de un alcance: lo mismo que la fila del panel del Calificador.</summary>
public class FuenteResumenDto
{
    public string Clave { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public EstadoFuente Estado { get; set; }
    public GrupoFuente Grupo { get; set; }
    public string? Nota { get; set; }
    public int Total { get; set; }
    public int Buscados { get; set; }
    public int Encontrados { get; set; }
    public int NoEncontrados { get; set; }
    public int FaltaPorBuscar { get; set; }
    public decimal? PctEncontrados { get; set; }
    public decimal? PctNoEncontrados { get; set; }
    public decimal? PctFaltaPorBuscar { get; set; }
    public DateTime? UltimaConsultaMax { get; set; }
    public DateTime? UltimoCargueOk { get; set; }
}

public class AlertaLocalizacionDto
{
    /// <summary>POR_BUSCAR | DATO_VIEJO | SIN_CONTACTO_SALDO.</summary>
    public string Tipo { get; set; } = string.Empty;
    /// <summary>alta | media.</summary>
    public string Severidad { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Fuente { get; set; }
    public int? LiderID { get; set; }
    public int? CarteraID { get; set; }
}

// ── Resumen ───────────────────────────────────────────────────────────────────

public class ResumenLocalizacionDto
{
    public CorteLocalizacionDto Corte { get; set; } = new();
    /// <summary>global | lider | cartera | par.</summary>
    public string Alcance { get; set; } = string.Empty;
    public string EtiquetaAlcance { get; set; } = string.Empty;
    public MetricasLocalizacionDto Metricas { get; set; } = new();
    public List<FuenteResumenDto> Fuentes { get; set; } = [];
    public List<AlertaLocalizacionDto> Alertas { get; set; } = [];
    public PesosIndiceDto Pesos { get; set; } = new();
}

// ── Cobertura ─────────────────────────────────────────────────────────────────

public class FilaCoberturaDto
{
    /// <summary>Clave del grupo ("6-8" par, "c8" cartera, "l6" líder, "l0" todos).</summary>
    public string Grupo { get; set; } = string.Empty;
    public string EtiquetaGrupo { get; set; } = string.Empty;
    public int? LiderID { get; set; }
    public string? NombreLider { get; set; }
    public long? DocumentoLider { get; set; }
    public int? CarteraID { get; set; }
    public string? NombreCartera { get; set; }
    /// <summary>La fila es un par concreto: admite la descarga por celda del Calificador (ADR-019).</summary>
    public bool EsPar { get; set; }
    public FuenteResumenDto Fuente { get; set; } = new();
    /// <summary>Buscados / Total.</summary>
    public decimal? PctCobertura { get; set; }
    /// <summary>Encontrados / Buscados.</summary>
    public decimal? PctExito { get; set; }
    /// <summary>Encontrados / Total.</summary>
    public decimal? PctLocalizacion { get; set; }
}

public class CoberturaLocalizacionDto
{
    public CorteLocalizacionDto Corte { get; set; } = new();
    /// <summary>par | cartera | lider.</summary>
    public string Agrupar { get; set; } = string.Empty;
    public List<FilaCoberturaDto> Filas { get; set; } = [];
}

// ── Frescura ──────────────────────────────────────────────────────────────────

public class FilaFrescuraDto
{
    public string Clave { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public EstadoFuente Estado { get; set; }
    public int Total { get; set; }
    public int Buscados { get; set; }
    public int Hasta90 { get; set; }
    public int De91a180 { get; set; }
    public int De181a365 { get; set; }
    public int Mas365 { get; set; }
    /// <summary>Buscados cuya última consulta supera el umbral pedido (o no tiene fecha).</summary>
    public int Viejos { get; set; }
    /// <summary>Viejos / Buscados.</summary>
    public decimal? PctViejos { get; set; }
    public DateTime? UltimaConsultaMax { get; set; }
    public DateTime? UltimoCargueOk { get; set; }
}

public class FrescuraLocalizacionDto
{
    public CorteLocalizacionDto Corte { get; set; } = new();
    public string Alcance { get; set; } = string.Empty;
    public string EtiquetaAlcance { get; set; } = string.Empty;
    /// <summary>90 | 180 | 365.</summary>
    public int Umbral { get; set; }
    public List<FilaFrescuraDto> Fuentes { get; set; } = [];
}

// ── Rankings ──────────────────────────────────────────────────────────────────

public class FilaRankingLiderDto
{
    public int Posicion { get; set; }
    public int LiderID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Cédula para la foto (<c>GET /api/personas/{documento}/foto</c>); null sin documento.</summary>
    public long? Documento { get; set; }
    /// <summary>Valor del criterio (0–100).</summary>
    public decimal? Valor { get; set; }
    /// <summary>Valor − valor del corte del día anterior (puntos); null si no hay con qué comparar.</summary>
    public decimal? Delta { get; set; }
    public int Total { get; set; }
    public decimal? Indice { get; set; }
    public decimal? PctContacto { get; set; }
    public decimal? PctCobertura { get; set; }
    public decimal? PctFrescura { get; set; }
    public int SinContactoConSaldo { get; set; }
    public decimal SaldoSinContacto { get; set; }
    /// <summary>Es la fila del Líder que consulta.</summary>
    public bool EsPropio { get; set; }
}

public class FilaRankingCarteraDto
{
    public int Posicion { get; set; }
    public int CarteraID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Para el Líder: su par en esa cartera, no la cartera completa.</summary>
    public int? LiderID { get; set; }
    public int Lideres { get; set; }
    public decimal? Valor { get; set; }
    public decimal? Delta { get; set; }
    public int Total { get; set; }
    public decimal? Indice { get; set; }
    public decimal? PctContacto { get; set; }
    public decimal? PctCobertura { get; set; }
    public decimal? PctFrescura { get; set; }
    public int SinContactoConSaldo { get; set; }
    public decimal SaldoSinContacto { get; set; }
}

public class RankingsLocalizacionDto
{
    public CorteLocalizacionDto Corte { get; set; } = new();
    /// <summary>Corte contra el que se calculan las variaciones (el último OK de un día anterior).</summary>
    public CorteLocalizacionDto? CorteAnterior { get; set; }
    public string Criterio { get; set; } = string.Empty;
    public bool MenorEsMejor { get; set; }
    public bool IncluirExternas { get; set; }
    public List<FilaRankingLiderDto> Lideres { get; set; } = [];
    public int TotalLideres { get; set; }
    /// <summary>Posición del Líder que consulta (null para Admin/Director o si no aparece).</summary>
    public int? MiPosicion { get; set; }
    /// <summary>La lista de líderes no está completa (vista de Líder: top + su fila).</summary>
    public bool Recortado { get; set; }
    /// <summary>Admin/Director: ranking de carteras. Líder: sus pares, entre ellos.</summary>
    public List<FilaRankingCarteraDto> Carteras { get; set; } = [];
    public bool CarterasSonPropias { get; set; }
}

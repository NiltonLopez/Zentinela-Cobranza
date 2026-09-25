namespace Zentinela.Application.DTOs.InformesLocalizacion;

// DTOs de GET /api/informes/localizacion/sugerencias (ADR-040, etapa 2). Las sugerencias se calculan en
// línea sobre los titulares del par: NO dependen de las tablas del precálculo.

/// <summary>
/// El ETL de estado de titulares (<c>vw_EtlEstadoTitularEstado</c>) está corriendo: saldo, perfil y
/// última gestión pueden estar a medio cargar. El controlador responde 409.
/// </summary>
public sealed class LocalizacionEtlEnCursoException(DateTime? inicio)
    : Exception("Los datos de gestión (saldo, perfil y última gestión) se están actualizando en este momento"
              + (inicio is { } i ? $" (desde las {i:HH:mm})" : "")
              + ". Las sugerencias dependen de ellos: vuelve a intentarlo en unos minutos.")
{
    public DateTime? Inicio { get; } = inicio;
}

/// <summary>Parámetros de una consulta de sugerencias (ya validados por el controlador).</summary>
public sealed class ParametrosSugerenciasLocalizacion
{
    /// <summary>Null = cartera completa (solo Admin/Director). Para el Líder, siempre el suyo.</summary>
    public int? LiderID { get; set; }
    public int CarteraID { get; set; }

    /// <summary>Claves del catálogo (solo fuentes Funcionales). Vacío = todas las Funcionales del panel.</summary>
    public List<string> Fuentes { get; set; } = [];

    /// <summary>"No encontrado y viejo": la última consulta es anterior a hoy − este número de días.</summary>
    public int UmbralDias { get; set; } = 180;

    /// <summary>"Encontrado pero viejo": null = no se sugiere refrescar a quien ya fue encontrado.</summary>
    public int? UmbralRefrescoDias { get; set; }

    public bool SoloConSaldo { get; set; } = true;

    /// <summary>Filas (titular + fuente) que se devuelven, ordenadas por prioridad.</summary>
    public int Top { get; set; } = 500;

    /// <summary>Excluye a quien ya va en un lote ENVIADO/CERRADO de la misma fuente en estos días (0 = no excluir).</summary>
    public int ExcluirEnviadosDias { get; set; } = 60;

    /// <summary>No resta la penalización a quien ya tiene contacto fresco.</summary>
    public bool IncluirLocalizados { get; set; }
}

/// <summary>Respuesta de <c>GET sugerencias</c>.</summary>
public class SugerenciasLocalizacionDto
{
    public int? LiderID { get; set; }
    public int CarteraID { get; set; }
    /// <summary>"DELMAR EXPERTOS — DIEGO …" o "DELMAR EXPERTOS (cartera completa)".</summary>
    public string EtiquetaAlcance { get; set; } = string.Empty;

    /// <summary>Parámetros efectivos (fuentes resueltas, top recortado…).</summary>
    public ParametrosSugerenciasLocalizacion Parametros { get; set; } = new();

    /// <summary>Última carga OK del ETL de gestión ("Gestión al …").</summary>
    public DateTime? DatosGestionAl { get; set; }
    public int? HorasDesdeCargaGestion { get; set; }

    /// <summary>Hay registro de envíos (tablas <c>LocalizacionEnvio*</c>): si no, no se excluyó a nadie por envío.</summary>
    public bool RegistroEnviosDisponible { get; set; }

    /// <summary>Avisos para mostrar arriba de los resultados (datos viejos, sin registro de envíos, par sin saldo…).</summary>
    public List<string> Avisos { get; set; } = [];

    public KpisSugerenciasDto Kpis { get; set; } = new();

    public List<FilaSugerenciaDto> Filas { get; set; } = [];

    /// <summary>Hay más candidatos que el top devuelto.</summary>
    public bool HayMas { get; set; }

    public int DuracionMs { get; set; }
}

public class KpisSugerenciasDto
{
    /// <summary>Titulares del par (distintos; en cartera completa, de todos sus líderes activos).</summary>
    public int TitularesPar { get; set; }
    public int TitularesConSaldo { get; set; }
    /// <summary>Percentil 90 del saldo de los titulares con saldo del par (base del puntaje por saldo).</summary>
    public decimal? P90Saldo { get; set; }

    /// <summary>Candidatos (titular + fuente) antes de exclusiones.</summary>
    public int CandidatosAntes { get; set; }
    /// <summary>Candidatos (titular + fuente) que quedan.</summary>
    public int Candidatos { get; set; }
    public int TitularesCandidatos { get; set; }
    /// <summary>Saldo de los titulares candidatos (cada titular una vez).</summary>
    public decimal SaldoCandidatos { get; set; }
    /// <summary>Candidatos que ya tienen contacto fresco (penalizados salvo "incluir localizados").</summary>
    public int ConContactoFresco { get; set; }

    public List<ConteoCategoriaSugerenciaDto> PorCategoria { get; set; } = [];
    public List<ConteoFuenteSugerenciaDto> PorFuente { get; set; } = [];
    public List<ConteoExclusionSugerenciaDto> Exclusiones { get; set; } = [];
}

public class ConteoCategoriaSugerenciaDto
{
    /// <summary>NUNCA_BUSCADO | NO_ENC_VIEJO | ENC_VIEJO.</summary>
    public string Categoria { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public int Antes { get; set; }
    public int Candidatos { get; set; }
}

public class ConteoFuenteSugerenciaDto
{
    public string Fuente { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public EstadoFuente Estado { get; set; }

    /// <summary>Antes de exclusiones. <see cref="NuncaAntes"/> = "Falta por buscar" del Calificador para el par.</summary>
    public int NuncaAntes { get; set; }
    public int NoEncontradoViejoAntes { get; set; }
    public int EncontradoViejoAntes { get; set; }
    public int Excluidos { get; set; }

    public int Nunca { get; set; }
    public int NoEncontradoViejo { get; set; }
    public int EncontradoViejo { get; set; }
    public int Candidatos { get; set; }
    public int ConContactoFresco { get; set; }
    public decimal SaldoCandidatos { get; set; }
}

public class ConteoExclusionSugerenciaDto
{
    /// <summary>YA_PAGO | FALLECIDO | NO_GESTIONAR | RECLAMACION | ACUERDO | PROMESA | ENVIADO | SIN_SALDO.</summary>
    public string Motivo { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    /// <summary>Candidatos (titular + fuente) que quitó.</summary>
    public int Filas { get; set; }
    public int Titulares { get; set; }
}

/// <summary>Una sugerencia: buscar a este titular en esta fuente.</summary>
public class FilaSugerenciaDto
{
    public long TitularID { get; set; }
    public string? Nombre { get; set; }
    public int CarteraID { get; set; }
    public string Cartera { get; set; } = string.Empty;
    public int LiderID { get; set; }
    public string Lider { get; set; } = string.Empty;
    public decimal? Saldo { get; set; }
    public string? UltimoPerfil { get; set; }
    public DateTime? FechaUltimaGestion { get; set; }

    public string Fuente { get; set; } = string.Empty;
    public string EtiquetaFuente { get; set; } = string.Empty;
    /// <summary>NUNCA_BUSCADO | NO_ENC_VIEJO | ENC_VIEJO.</summary>
    public string Categoria { get; set; } = string.Empty;
    /// <summary>Fecha de la última fila del titular en la fuente (null = nunca buscado).</summary>
    public DateTime? UltimaConsulta { get; set; }

    public int Prioridad { get; set; }
    public DesglosePrioridadDto Desglose { get; set; } = new();

    /// <summary>Etiquetas de las fuentes Funcionales donde su última fila ya es "encontrado".</summary>
    public List<string> EncontradoEn { get; set; } = [];
}

public class DesglosePrioridadDto
{
    public int Perfil { get; set; }
    public int Saldo { get; set; }
    public int Gestion { get; set; }
    public int NuncaFuncional { get; set; }
    public int NuncaFuente { get; set; }
    public int NoEncontradoViejo { get; set; }
    /// <summary>Negativo (−100) si ya tiene contacto fresco y no se pidió "incluir localizados".</summary>
    public int ContactoFresco { get; set; }
}

/// <summary>Una fila del Excel del cargue: el titular y su par; las columnas de dato van vacías.</summary>
public sealed record FilaListaLocalizacion(long TitularID, string? Nombre, int CarteraID, string Cartera, int LiderID, string Lider);

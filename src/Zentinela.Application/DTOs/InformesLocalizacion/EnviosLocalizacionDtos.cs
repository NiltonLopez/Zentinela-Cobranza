namespace Zentinela.Application.DTOs.InformesLocalizacion;

// DTOs de los envíos a localización (ADR-040, etapa 3): lotes en LocalizacionEnvio / LocalizacionEnvioDetalle,
// su retorno (lo que volvió de la fuente) y su impacto (lo que pasó en la gestión después del envío).

// ── Excepciones que el controlador traduce a HTTP ─────────────────────────────

/// <summary>El lote no existe o no es del alcance de quien pregunta (un Líder no ve lotes ajenos). 404.</summary>
public sealed class LocalizacionEnvioNoEncontradoException(int envioID)
    : Exception($"No existe el lote {envioID} o no tienes acceso a él.")
{
    public int EnvioID { get; } = envioID;
}

/// <summary>El lote no está en un estado que admita la acción (otro usuario lo cambió antes). 409.</summary>
public sealed class LocalizacionEnvioConflictoException(string mensaje, string estadoActual) : Exception(mensaje)
{
    public string EstadoActual { get; } = estadoActual;
}

// ── Crear ─────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>POST envios</c>: los MISMOS parámetros de la consulta de sugerencias que el usuario tiene en pantalla, más
/// la fuente del lote. El servidor recalcula las sugerencias y toma las filas de esa fuente del top (lo que
/// se ve y lo que baja el Excel); no recibe TitularIDs del cliente.
/// </summary>
public sealed class CrearEnvioLocalizacionDto
{
    public int? LiderID { get; set; }
    public int CarteraID { get; set; }

    /// <summary>Fuente del lote (una sola; clave del catálogo, Funcional).</summary>
    public string Fuente { get; set; } = string.Empty;

    // Parámetros de la consulta de sugerencias (los de GET sugerencias).
    public List<string> Fuentes { get; set; } = [];
    public int UmbralDias { get; set; } = 180;
    public int? UmbralRefrescoDias { get; set; }
    public bool SoloConSaldo { get; set; } = true;
    public int Top { get; set; } = 500;
    public int ExcluirEnviadosDias { get; set; } = 60;
    public bool IncluirLocalizados { get; set; }

    /// <summary>Nota libre del líder para el departamento (hasta 500 caracteres).</summary>
    public string? Observacion { get; set; }
}

/// <summary>Respuesta de <c>POST envios</c>.</summary>
public class EnvioCreadoDto
{
    public int EnvioID { get; set; }
    /// <summary>LOC-000123.</summary>
    public string Codigo { get; set; } = string.Empty;
    public int Titulares { get; set; }
    public List<string> Avisos { get; set; } = [];
}

/// <summary>Cuerpo opcional de <c>POST envios/{id}/anular</c>.</summary>
public sealed class AnularEnvioLocalizacionDto
{
    public string? Motivo { get; set; }
}

// ── Lote ──────────────────────────────────────────────────────────────────────

/// <summary>Un lote (cabecera) con su retorno e impacto cuando ya se envió.</summary>
public class EnvioLocalizacionDto
{
    public int EnvioID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int LiderID { get; set; }
    public string Lider { get; set; } = string.Empty;
    /// <summary>Cédula del líder para la foto.</summary>
    public long? DocumentoLider { get; set; }
    public int CarteraID { get; set; }
    public string Cartera { get; set; } = string.Empty;
    public string Fuente { get; set; } = string.Empty;
    public string EtiquetaFuente { get; set; } = string.Empty;
    /// <summary>BORRADOR | ENVIADO | CERRADO | ANULADO.</summary>
    public string Estado { get; set; } = string.Empty;
    public int Titulares { get; set; }
    /// <summary>JSON con los parámetros de la sugerencia que originó el lote.</summary>
    public string? Parametros { get; set; }
    public string? Observacion { get; set; }
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public string? EnviadoPor { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string? CerradoPor { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string? AnuladoPor { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    public string? MotivoAnulacion { get; set; }

    /// <summary>Null mientras no se envíe.</summary>
    public RetornoEnvioDto? Retorno { get; set; }
    /// <summary>Null mientras no se envíe.</summary>
    public ImpactoEnvioDto? Impacto { get; set; }
}

/// <summary>Lo que volvió de la fuente después del envío.</summary>
public class RetornoEnvioDto
{
    /// <summary>CARGUE = hay un cargue OK de la fuente después del envío; FECHA = no lo hay y se cuenta todo dato posterior al envío.</summary>
    public string Base { get; set; } = string.Empty;
    public long? CargueEjecucionID { get; set; }
    /// <summary>Fin del cargue tomado como retorno.</summary>
    public DateTime? FechaCargue { get; set; }
    /// <summary>Datos de la fuente desde esta fecha cuentan como "buscado tras el envío" (sello del cargue, o la fecha del envío).</summary>
    public DateTime BuscadosDesde { get; set; }
    /// <summary>Titulares del lote con dato nuevo en la fuente.</summary>
    public int Buscados { get; set; }
    /// <summary>Buscados / titulares del lote.</summary>
    public decimal? PctRetorno { get; set; }
    /// <summary>Buscados cuya última fila cumple el predicado de "encontrado" del catálogo.</summary>
    public int Localizados { get; set; }
    /// <summary>Localizados / buscados.</summary>
    public decimal? PctLocalizadosBuscados { get; set; }
    /// <summary>Localizados / titulares del lote.</summary>
    public decimal? PctLocalizados { get; set; }
    /// <summary>Fin del cargue de retorno o, sin cargue, primer dato nuevo; null = todavía no volvió nada.</summary>
    public DateTime? FechaRetorno { get; set; }
    public int? DiasRetorno { get; set; }
}

/// <summary>Lo que pasó en la gestión después del envío (UltimoEstadoTitular y Pago).</summary>
public class ImpactoEnvioDto
{
    /// <summary>Con una gestión posterior al día del envío.</summary>
    public int Gestionados { get; set; }
    public decimal? PctGestionados { get; set; }
    /// <summary>Gestionados cuyo UltimoPerfil pasó de "sin contacto" (en la foto del envío) a "con contacto".</summary>
    public int PasaronAContacto { get; set; }
    /// <summary>Titulares con al menos un pago posterior al día del envío (cualquier cartera).</summary>
    public int TitularesConPago { get; set; }
    public int Pagos { get; set; }
    public decimal ValorPagado { get; set; }
    public decimal Honorarios { get; set; }
    /// <summary>Con una promesa vigente (FechaPromesa ≥ hoy) distinta de la que tenían al enviarlos.</summary>
    public int PromesasVigentes { get; set; }
}

/// <summary>Un titular del lote: la foto al enviarlo y lo que pasó después.</summary>
public class FilaDetalleEnvioDto
{
    public long TitularID { get; set; }
    public string? Nombre { get; set; }
    /// <summary>NUNCA_BUSCADO | NO_ENC_VIEJO | ENC_VIEJO.</summary>
    public string Categoria { get; set; } = string.Empty;
    public int Prioridad { get; set; }
    public DateTime? UltimaConsultaPrevia { get; set; }

    // Foto al crear el lote.
    public decimal? SaldoFoto { get; set; }
    public string? PerfilFoto { get; set; }
    public DateTime? GestionFoto { get; set; }

    // Después del envío.
    public bool Buscado { get; set; }
    public bool Localizado { get; set; }
    /// <summary>Fecha de la última fila del titular en la fuente.</summary>
    public DateTime? FechaDato { get; set; }
    public string? PerfilActual { get; set; }
    public DateTime? GestionActual { get; set; }
    public bool Gestionado { get; set; }
    public bool PasoAContacto { get; set; }
    public decimal ValorPagado { get; set; }
    public DateTime? PromesaVigente { get; set; }
}

/// <summary><c>GET envios/{id}</c>: el lote con una página de su detalle.</summary>
public class DetalleEnvioLocalizacionDto
{
    public EnvioLocalizacionDto Envio { get; set; } = new();
    public List<FilaDetalleEnvioDto> Filas { get; set; } = [];
    public int Pagina { get; set; }
    public int TamPagina { get; set; }
    public int TotalFilas { get; set; }
}

// ── Lista ─────────────────────────────────────────────────────────────────────

/// <summary>Filtros de <c>GET envios</c>. El LiderID del Líder lo fija el controlador.</summary>
public sealed class FiltroEnviosLocalizacion
{
    public int? LiderID { get; set; }
    public int? CarteraID { get; set; }
    public string? Fuente { get; set; }
    /// <summary>BORRADOR | ENVIADO | CERRADO | ANULADO; null = todos.</summary>
    public string? Estado { get; set; }
    /// <summary>Por FechaCreacion.</summary>
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
}

public class ListaEnviosLocalizacionDto
{
    public List<EnvioLocalizacionDto> Envios { get; set; } = [];
    /// <summary>Hay más lotes que el máximo que devuelve la lista (los más recientes primero).</summary>
    public bool Recortado { get; set; }
}

// ── Uso del departamento ──────────────────────────────────────────────────────

/// <summary>Uso del departamento de localización de un líder (o el total) en el período.</summary>
public class FilaUsoLiderDto
{
    /// <summary>0 en la fila de total.</summary>
    public int LiderID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public long? Documento { get; set; }
    /// <summary>Lotes enviados en el período (ENVIADO o CERRADO, por FechaEnvio).</summary>
    public int Lotes { get; set; }
    /// <summary>Lotes en BORRADOR (sin enviar), sin importar la fecha.</summary>
    public int Borradores { get; set; }
    /// <summary>Filas enviadas (titular × fuente).</summary>
    public int Enviados { get; set; }
    /// <summary>Cola sugerida aproximada del último corte (ver ADR-040); null si no hay corte.</summary>
    public int? Cola { get; set; }
    public decimal? PctColaEnviada { get; set; }
    public int Buscados { get; set; }
    public decimal? PctRetorno { get; set; }
    public int Localizados { get; set; }
    public decimal? PctLocalizados { get; set; }
    public int Gestionados { get; set; }
    public int PasaronAContacto { get; set; }
    public int TitularesConPago { get; set; }
    public decimal ValorPagado { get; set; }
    public decimal Honorarios { get; set; }
    public int PromesasVigentes { get; set; }
    public int LotesConRetorno { get; set; }
    public decimal? DiasRetornoPromedio { get; set; }
}

public class UsoDepartamentoDto
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    /// <summary>Fecha del corte del que sale la cola aproximada (null = no hay corte).</summary>
    public DateTime? CorteCola { get; set; }
    public List<FilaUsoLiderDto> Lideres { get; set; } = [];
    public FilaUsoLiderDto Total { get; set; } = new();
    public List<string> Avisos { get; set; } = [];
}

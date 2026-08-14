namespace Zentinela.Application.DTOs.Virtualidad.Historico;

// ── Fase Solicitud ──────────────────────────────────────────────────────────

/// <summary>Fila del archivo de solicitudes, ya resuelta a cartera/líder.</summary>
public class FilaSolicitudDto
{
    public long     NumeroDocumento { get; set; }
    public string   Destino         { get; set; } = string.Empty;
    public string   NombreCartera   { get; set; } = string.Empty; // tal como vino en el archivo / resuelto
    public string   NombreLider     { get; set; } = string.Empty;
    public int      CarteraID       { get; set; }
    public int      LiderID         { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    public bool     EsValida        { get; set; }
    public string?  ErrorMensaje    { get; set; }
}

public class AlertaSolicitudDto
{
    public string Destino       { get; set; } = string.Empty;
    public string NombreCartera { get; set; } = string.Empty;
    public int    TotalEnvios   { get; set; }
}

/// <summary>Un tipo de error con su conteo de filas — evita repetir el mismo
/// mensaje una vez por fila cuando muchas comparten la misma causa.</summary>
public class ErrorAgrupadoDto
{
    public string Mensaje  { get; set; } = string.Empty;
    public int    Cantidad { get; set; }
}

public class ValidarSolicitudResponse
{
    public int                      TotalFilas       { get; set; }
    public int                      TotalValidas     { get; set; }
    public int                      TotalErrores     { get; set; }
    public List<FilaSolicitudDto>   Preview          { get; set; } = [];
    public List<FilaSolicitudDto>   FilasValidas     { get; set; } = [];
    public List<AlertaSolicitudDto> Alertas          { get; set; } = [];
    /// <summary>Todos los errores del archivo agrupados por mensaje — no se limita
    /// a la muestra de <see cref="Preview"/>.</summary>
    public List<ErrorAgrupadoDto>   ErroresAgrupados { get; set; } = [];
}

public class ConfirmarSolicitudRequest
{
    public string                 Canal        { get; set; } = string.Empty;
    public List<FilaSolicitudDto> FilasValidas { get; set; } = [];
}

// ── Fase Resultado ──────────────────────────────────────────────────────────

/// <summary>Fila del archivo de resultados: ID de la solicitud + Estado a aplicar.</summary>
public class FilaResultadoDto
{
    public long    ID           { get; set; }
    public string  Estado       { get; set; } = string.Empty;
    public bool    EsValida     { get; set; }
    public string? ErrorMensaje { get; set; }
}

public class ValidarResultadoResponse
{
    public int                    TotalFilas       { get; set; }
    public int                    TotalValidas     { get; set; }
    public int                    TotalErrores     { get; set; }
    public List<FilaResultadoDto> Preview          { get; set; } = [];
    public List<FilaResultadoDto> FilasValidas     { get; set; } = [];
    /// <summary>Todos los errores del archivo agrupados por mensaje — no se limita
    /// a la muestra de <see cref="Preview"/>.</summary>
    public List<ErrorAgrupadoDto> ErroresAgrupados { get; set; } = [];
}

public class ConfirmarResultadoRequest
{
    public List<FilaResultadoDto> FilasValidas { get; set; } = [];
}

// ── Catálogo (lo consume el frontend para no duplicar estados/columnas) ──────

public class MapeoEstadoDto
{
    public string CodigoCrudo { get; set; } = string.Empty;   // "Dial"
    public string Estado      { get; set; } = string.Empty;   // "OPCION 1"
    public string Significado { get; set; } = string.Empty;
}

public class CatalogoCanalDto
{
    public string               Canal                { get; set; } = string.Empty;
    public ModoCargaCanal       Modo                 { get; set; }
    public string[]             Estados              { get; set; } = [];
    /// <summary>Solo para canales de archivo único; vacío en los de dos fases.</summary>
    public List<MapeoEstadoDto> MapeoEstados         { get; set; } = [];
    public string[]             ColumnasEsperadas    { get; set; } = [];
    public string[]             ExtensionesAceptadas { get; set; } = [];
}

// ── Carga única (canal IVR) ─────────────────────────────────────────────────

/// <summary>Fila del archivo del marcador, ya resuelta a cartera/líder y con el estado traducido.</summary>
public class FilaIvrDto
{
    public long     NumeroDocumento { get; set; }
    public string   Destino         { get; set; } = string.Empty;
    public string   NombreCartera   { get; set; } = string.Empty;
    public string   NombreLider     { get; set; } = string.Empty;
    public int      CarteraID       { get; set; }
    public int      LiderID         { get; set; }
    public DateOnly FechaEnvio      { get; set; }
    /// <summary>Valor crudo de la columna "Last App", tal como vino en el archivo.</summary>
    public string   CodigoCrudo     { get; set; } = string.Empty;
    public string?  Estado          { get; set; }
    public bool     EsValida        { get; set; }
    public string?  ErrorMensaje    { get; set; }
}

public class ConteoEstadoDto
{
    public string  Estado      { get; set; } = string.Empty;
    public string  CodigoCrudo { get; set; } = string.Empty;
    public int     Cantidad    { get; set; }
    public decimal Porcentaje  { get; set; }
}

public class ConteoCarteraDto
{
    public string NombreCartera { get; set; } = string.Empty;
    public string NombreLider   { get; set; } = string.Empty;
    public int    Cantidad      { get; set; }
}

/// <summary>
/// Registros del canal que YA existen en BD para la misma cartera y rango de fechas del archivo.
/// Es un aviso de posible recarga — NO bloquea, porque en IVR el mismo número puede marcarse
/// legítimamente varias veces y no hay clave natural con la cual deduplicar.
/// </summary>
public class SolapamientoDto
{
    public int    CarteraID     { get; set; }
    public string NombreCartera { get; set; } = string.Empty;
    public int    EnArchivo     { get; set; }
    public int    YaEnBaseDatos { get; set; }
}

public class ValidarCargaIvrResponse
{
    public int TotalFilas   { get; set; }
    public int TotalValidas { get; set; }
    public int TotalErrores { get; set; }
    /// <summary>Filas rechazadas porque su "Last App" no está en el catálogo. Subconjunto de TotalErrores.</summary>
    public int TotalCodigoDesconocido { get; set; }

    /// <summary>SHA-256 del archivo — se exige idéntico al confirmar.</summary>
    public string HuellaArchivo { get; set; } = string.Empty;

    public List<FilaIvrDto>       Preview             { get; set; } = [];
    public List<ConteoEstadoDto>  DesglosePorEstado   { get; set; } = [];
    public List<ConteoCarteraDto> DesglosePorCartera  { get; set; } = [];
    public List<string>           CodigosDesconocidos { get; set; } = [];

    public DateOnly? FechaMin { get; set; }
    public DateOnly? FechaMax { get; set; }

    public List<SolapamientoDto>    Solapamientos    { get; set; } = [];
    public List<AlertaSolicitudDto> Alertas          { get; set; } = [];
    public List<ErrorAgrupadoDto>   ErroresAgrupados { get; set; } = [];

    public List<string> ColumnasEncontradas { get; set; } = [];
    public List<string> ColumnasFaltantes   { get; set; } = [];

    /// <summary>Filas idénticas repetidas dentro del propio archivo (señal de archivo concatenado).</summary>
    public int FilasRepetidasEnArchivo { get; set; }
}

public class ConfirmarCargaIvrResponse
{
    public int      Insertados  { get; set; }
    public int      Descartados { get; set; }
    /// <summary>Sello uniforme del lote: permite revertirlo con un DELETE por este valor.</summary>
    public DateTime FechaCargue { get; set; }
    public string   Mensaje     { get; set; } = string.Empty;
}

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

public class ValidarSolicitudResponse
{
    public int                     TotalFilas   { get; set; }
    public int                     TotalValidas { get; set; }
    public int                     TotalErrores { get; set; }
    public List<FilaSolicitudDto>  Preview      { get; set; } = [];
    public List<FilaSolicitudDto>  FilasValidas { get; set; } = [];
    public List<AlertaSolicitudDto> Alertas     { get; set; } = [];
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
    public int                    TotalFilas   { get; set; }
    public int                    TotalValidas { get; set; }
    public int                    TotalErrores { get; set; }
    public List<FilaResultadoDto> Preview      { get; set; } = [];
    public List<FilaResultadoDto> FilasValidas { get; set; } = [];
}

public class ConfirmarResultadoRequest
{
    public List<FilaResultadoDto> FilasValidas { get; set; } = [];
}

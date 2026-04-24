namespace Zentinela.Application.DTOs.Reportes;

public enum EstadoFilaReporte
{
    Ok,
    NuevoTitular,       // TitularID no existe pero tiene Nombre → se creará
    SinNombreParaCrear, // TitularID no existe y sin Nombre → error
    AgenteNoEncontrado,
}

/// <summary>Fila raw parseada del Excel antes de validar.</summary>
public class FilaReporteRaw
{
    public long?     TitularID          { get; set; }
    public string?   Nombre             { get; set; }   // obligatorio si es titular nuevo
    public int?      CarteraID          { get; set; }   // inyectado desde la UI
    /// <summary>Username de ControlNext leído del Excel. Se resuelve a AgenteID en validación.</summary>
    public string?   UsuarioControl     { get; set; }
    /// <summary>Resuelto desde UsuarioControl → Agente.UsuarioControlNext durante la validación.</summary>
    public long?     AgenteID           { get; set; }
    public string?   UltimoPerfil       { get; set; }
    public string?   MejorPerfil        { get; set; }

    public decimal?  SaldoTotal         { get; set; }
    public string?   NumeroMarcado      { get; set; }
    public int?      Rotacion           { get; set; }
    public DateOnly? FechaUltimaGestion { get; set; }
    public DateOnly? FechaPromesa       { get; set; }
    public decimal?  ValorPromesa       { get; set; }
    public string?   MotivoNoPago       { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}

/// <summary>Resultado de validación visible en la tabla de preview.</summary>
public class FilaValidacionReporteDto
{
    public string  TitularID      { get; set; } = string.Empty;
    public string? Nombre         { get; set; }
    public string  UsuarioControl { get; set; } = string.Empty;
    public EstadoFilaReporte Estado { get; set; }
    public string? Razon          { get; set; }
    /// <summary>
    /// Campos opcionales que tienen valor en esta fila (non-null en el archivo).
    /// El frontend muestra ✓ para los presentes y ✗ para los ausentes.
    /// </summary>
    public string[] CamposMapeados { get; set; } = [];
}

/// <summary>
/// Vista previa devuelta tras validar el Excel.
/// Filas: primeros 10 registros válidos (muestra).
/// FilasConError: TODAS las filas inválidas (sin límite) para mostrarlas todas al usuario.
/// FilasValidasRaw: lista completa de filas válidas para el confirm.
/// </summary>
public class PreviewReporteDto
{
    public List<FilaValidacionReporteDto> Filas               { get; set; } = [];
    public List<FilaValidacionReporteDto> FilasConError        { get; set; } = [];
    public int                            TotalValidos          { get; set; }
    public int                            TotalNuevos           { get; set; } // subconjunto de válidos
    public int                            TotalInvalidos        { get; set; }
    public List<FilaReporteRaw>           FilasValidasRaw       { get; set; } = [];
    /// <summary>
    /// Columnas opcionales cuyo encabezado fue reconocido en el archivo.
    /// Igual para todas las filas — el frontend muestra ✓/✗ en el header de cada columna.
    /// </summary>
    public string[]                       ColumnasEncontradas   { get; set; } = [];
    /// <summary>Filas válidas con clave (TitularID, CarteraID) única — las que realmente se procesarán.</summary>
    public int                            TotalUnicos           { get; set; }
    /// <summary>Filas válidas descartadas por ser duplicados de la misma clave (TitularID, CarteraID).</summary>
    public int                            TotalDuplicados       { get; set; }
}

/// <summary>Body del endpoint de confirmación.</summary>
public class ConfirmarReporteRequest
{
    public List<FilaReporteRaw> FilasValidas { get; set; } = [];
}

/// <summary>Resultado del cargue definitivo.</summary>
public class ResultadoReporteDto
{
    public int    Procesados      { get; set; }
    public int    TitularesNuevos { get; set; }
    public int    Duplicados      { get; set; }
    public string Mensaje         { get; set; } = string.Empty;
}

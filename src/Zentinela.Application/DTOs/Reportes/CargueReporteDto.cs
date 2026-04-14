namespace Zentinela.Application.DTOs.Reportes;

public enum EstadoFilaReporte
{
    Ok,
    NuevoTitular,       // TitularID no existe pero tiene Nombre → se creará
    SinNombreParaCrear, // TitularID no existe y sin Nombre → error
    AgenteIDInvalido,
}

/// <summary>Fila raw parseada del Excel antes de validar.</summary>
public class FilaReporteRaw
{
    public long?     TitularID          { get; set; }
    public string?   Nombre             { get; set; }   // obligatorio si es titular nuevo
    public int?      CarteraID          { get; set; }   // inyectado desde la UI
    public int?      AgenteID           { get; set; }
    public string?   UltimoPerfil       { get; set; }
    public string?   MejorPerfil        { get; set; }
    public DateOnly? FechaMejorPerfil   { get; set; }
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
    public string  TitularID { get; set; } = string.Empty;
    public string? Nombre    { get; set; }
    public string  AgenteID  { get; set; } = string.Empty;
    public EstadoFilaReporte Estado { get; set; }
    public string? Razon     { get; set; }
}

/// <summary>
/// Vista previa devuelta tras validar el Excel.
/// Filas contiene hasta 10 registros (muestra); FilasValidasRaw contiene la lista
/// completa de filas válidas (Ok + NuevoTitular) para el confirm.
/// </summary>
public class PreviewReporteDto
{
    public List<FilaValidacionReporteDto> Filas          { get; set; } = [];
    public int                            TotalValidos    { get; set; }
    public int                            TotalNuevos     { get; set; } // subconjunto de válidos
    public int                            TotalInvalidos  { get; set; }
    public List<FilaReporteRaw>           FilasValidasRaw { get; set; } = [];
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
    public string Mensaje         { get; set; } = string.Empty;
}

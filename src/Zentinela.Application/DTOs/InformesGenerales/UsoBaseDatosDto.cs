namespace Zentinela.Application.DTOs.InformesGenerales;

/// <summary>
/// Archivo entrante ya desacoplado de IFormFile — la capa Application no conoce ASP.NET.
/// El controller abre el stream y lo dispone al terminar.
/// </summary>
public sealed record ArchivoTelefonos(string NombreArchivo, string Extension, Stream Contenido);

/// <summary>
/// Resultado del cruce de una lista de teléfonos contra las fuentes de localización
/// de DBCallCenter, con la acreditación de uso agregada por fuente y por líder.
///
/// Convención de conteo (decidida con el negocio):
///   - <see cref="NumerosEnBaseDatos"/> cuenta números DISTINTOS — es el KPI principal.
///   - El desglose por líder acredita cada número ÍNTEGRO a cada líder que tenga a ese
///     titular asignado. Por eso <see cref="SumaAcreditaciones"/> ≥ <see cref="NumerosConLider"/>:
///     la diferencia son acreditaciones compartidas, no números adicionales.
/// </summary>
public class UsoBaseDatosDto
{
    // ── Ingesta ───────────────────────────────────────────────────────────────
    public List<ArchivoProcesadoDto> Archivos { get; set; } = [];

    public int TotalFilasLeidas         { get; set; }
    public int TotalTelefonosValidos    { get; set; }  // ocurrencias válidas (con repetidos)
    public int TotalTelefonosInvalidos  { get; set; }
    public int TelefonosDistintos       { get; set; }  // filas realmente cargadas a #Telefonos
    /// <summary>Σ de distintos por archivo − distintos globales: cuántos venían repetidos entre archivos.</summary>
    public int SolapamientoEntreArchivos { get; set; }
    public int TelefonosCelulares       { get; set; }  // de los distintos, cuántos empiezan en 3
    public int TelefonosFijos           { get; set; }

    // ── KPIs principales ──────────────────────────────────────────────────────
    public int     NumerosEnBaseDatos   { get; set; }
    public int     NumerosNoEncontrados { get; set; }
    public decimal PorcentajeUso        { get; set; }
    public int     TitularesDistintos   { get; set; }
    /// <summary>TitularID presente en una fuente pero ausente de dbo.Titular. Su número SÍ acredita uso.</summary>
    public int     TitularesHuerfanos   { get; set; }

    // ── Acreditación por líder ────────────────────────────────────────────────
    public int NumerosConLider    { get; set; }
    /// <summary>Bucket residual: encontrados en BD pero sin ninguna fila en Titular_Cartera. Nunca se descarta.</summary>
    public int NumerosSinLider    { get; set; }
    public int NumerosCompartidos { get; set; }  // pertenecen a titulares de > 1 líder
    public int NumerosUnSoloLider { get; set; }
    public int LideresDistintos   { get; set; }
    /// <summary>Σ de PorLider.NumerosAcreditados (sin contar la fila "sin líder").</summary>
    public int SumaAcreditaciones { get; set; }

    // ── Desgloses ─────────────────────────────────────────────────────────────
    public List<UsoPorFuenteDto>        PorFuente       { get; set; } = [];
    public List<UsoPorLiderDto>         PorLider        { get; set; } = [];
    public List<UsoPorLiderCarteraDto>  PorLiderCartera { get; set; } = [];
    public List<UsoNumeroCompartidoDto> TopCompartidos  { get; set; } = [];  // TOP 50
    public List<string>                 MuestraNoEncontrados { get; set; } = [];  // TOP 100

    public List<string> Advertencias { get; set; } = [];
    public long         DuracionMs   { get; set; }
}

public class ArchivoProcesadoDto
{
    public string  NombreArchivo    { get; set; } = string.Empty;
    public bool    Ok               { get; set; }
    /// <summary>Motivo del fallo cuando Ok = false, o aviso cuando Ok = true.</summary>
    public string? Mensaje          { get; set; }
    /// <summary>Encabezado original (tal como venía en el archivo) del que se leyeron los teléfonos.</summary>
    public string? ColumnaDetectada { get; set; }

    public int FilasLeidas        { get; set; }
    public int TelefonosValidos   { get; set; }
    public int TelefonosInvalidos { get; set; }
    public int TelefonosDistintos { get; set; }
}

public class UsoPorFuenteDto
{
    public string  Fuente             { get; set; } = string.Empty;  // "Cajacopi"
    public string  Etiqueta           { get; set; } = string.Empty;  // "Cajacopi (celular)"
    public int     NumerosDistintos   { get; set; }
    public int     TitularesDistintos { get; set; }
    public int     Apariciones        { get; set; }
    public decimal Porcentaje         { get; set; }  // sobre NumerosEnBaseDatos
}

public class UsoPorLiderDto
{
    /// <summary>null = fila agregada "(sin líder asignado)".</summary>
    public int?    LiderID            { get; set; }
    public string  Lider              { get; set; } = string.Empty;
    public int     NumerosAcreditados { get; set; }
    public int     TitularesDistintos { get; set; }
    public int     CarterasDistintas  { get; set; }

    /// <summary>
    /// Titulares que el líder tiene asignados en Titular_Cartera (todas sus carteras).
    /// Es el denominador del uso real: el tamaño de la base que le corresponde trabajar.
    /// </summary>
    public int     TitularesAsignados { get; set; }

    /// <summary>
    /// Uso real de SU base: TitularesDistintos / TitularesAsignados.
    /// Se mide en titulares (no en números) para que sea dimensionalmente coherente con
    /// el denominador — un titular puede tener varios teléfonos y daría más de 100 %.
    /// null cuando no hay asignación contra la cual medir (fila "(sin líder asignado)").
    /// </summary>
    public decimal? PorcentajeCobertura { get; set; }
    /// <summary>Alguna de sus relaciones Lider_Cartera está inactivada (ADR-022). Solo enriquecimiento.</summary>
    public bool    RelacionInactiva   { get; set; }
    /// <summary>
    /// El par (LiderID, CarteraID) existe en Titular_Cartera pero NO en Lider_Cartera.
    /// Centinela contra la clase de bug de InformesRecaudoService.GetInformeLideresAsync:
    /// la fila se marca, nunca se descarta.
    /// </summary>
    public bool    SinRelacionLiderCartera { get; set; }
    public decimal Porcentaje         { get; set; }
}

public class UsoPorLiderCarteraDto
{
    public int?   LiderID   { get; set; }
    public string Lider     { get; set; } = string.Empty;
    public int?   CarteraID { get; set; }
    public string Cartera   { get; set; } = string.Empty;

    public int  NumerosAcreditados { get; set; }
    public int  TitularesDistintos { get; set; }
    /// <summary>Titulares asignados al líder EN ESA CARTERA (no en todas).</summary>
    public int  TitularesAsignados { get; set; }
    /// <summary>Uso real de esa cartera: TitularesDistintos / TitularesAsignados.</summary>
    public decimal? PorcentajeCobertura { get; set; }
    public bool EsExterna               { get; set; }
    public bool RelacionInactiva        { get; set; }
    public bool SinRelacionLiderCartera { get; set; }
}

public class UsoNumeroCompartidoDto
{
    public string Numero         { get; set; } = string.Empty;
    public int    Lideres        { get; set; }
    public int    Titulares      { get; set; }
    public string LideresNombres { get; set; } = string.Empty;
}

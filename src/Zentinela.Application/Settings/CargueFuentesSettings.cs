namespace Zentinela.Application.Settings;

/// <summary>
/// Configuración tipada — se carga desde appsettings.json sección "CargueFuentes".
/// Usar con IOptions&lt;CargueFuentesSettings&gt; en lugar de IConfiguration directamente.
/// </summary>
public class CargueFuentesSettings
{
    /// <summary>
    /// Carpeta donde se guarda el archivo subido entre validar y confirmar. Vacío = subcarpeta
    /// "zentinela-cargues" del temp del proceso (con IIS suele ser C:\Windows\Temp del AppPool;
    /// si ese disco es chico, configurar otra).
    /// </summary>
    public string CarpetaTemporal { get; set; } = string.Empty;

    /// <summary>Horas que se conservan jobs terminados y sus carpetas.</summary>
    public int RetencionHoras { get; set; } = 48;

    /// <summary>
    /// Tope de filas para XLSX: ClosedXML carga el libro entero en memoria (varios GB con
    /// 1M de filas). Por encima se pide CSV, que se lee por streaming y no tiene tope.
    /// </summary>
    public int LimiteFilasXlsx { get; set; } = 250_000;

    /// <summary>Jobs activos (validando o cargando) en todo el servidor.</summary>
    public int MaxJobsSimultaneos { get; set; } = 2;

    /// <summary>Carpeta efectiva: la configurada o la del temp del proceso.</summary>
    public string ResolverCarpetaTemporal() =>
        string.IsNullOrWhiteSpace(CarpetaTemporal)
            ? Path.Combine(Path.GetTempPath(), "zentinela-cargues")
            : CarpetaTemporal;
}

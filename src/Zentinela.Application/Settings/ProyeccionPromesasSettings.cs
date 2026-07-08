namespace Zentinela.Application.Settings;

/// <summary>
/// Configuración tipada — se carga desde appsettings.json sección "ProyeccionPromesas".
/// Usar con IOptions&lt;ProyeccionPromesasSettings&gt; en lugar de IConfiguration directamente.
/// </summary>
public class ProyeccionPromesasSettings
{
    public decimal TechoAbsolutoPorTitular { get; set; } = 25_000_000m;
}

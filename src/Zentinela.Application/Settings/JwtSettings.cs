namespace Zentinela.Application.Settings;

/// <summary>
/// Configuración JWT tipada — se carga desde appsettings.json sección "JwtSettings".
/// Usar con IOptions&lt;JwtSettings&gt; en lugar de IConfiguration directamente.
/// </summary>
public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationHours { get; set; } = 8;
}

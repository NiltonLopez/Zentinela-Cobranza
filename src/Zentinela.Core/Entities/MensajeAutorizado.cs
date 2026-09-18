namespace Zentinela.Core.Entities;

/// <summary>
/// Texto de SMS autorizado por el área legal. Es un catálogo cerrado: el líder elige uno,
/// nunca redacta. El texto lleva placeholders {NOMBRE}, {CELULAR_ASESOR} y {LINK_ASESOR};
/// los dos del asesor son obligatorios (CHECK en BD y validación en la API).
/// No se borra físicamente: EstrategiaSms lo referencia. Se desactiva.
/// </summary>
public class MensajeAutorizado
{
    public int      ID                   { get; set; }
    public string   Titulo               { get; set; } = string.Empty;
    public string   Texto                { get; set; } = string.Empty;
    public bool     Activo               { get; set; } = true;
    public DateTime FechaCreacion        { get; set; }
    public string   UsuarioCreacion      { get; set; } = string.Empty;
    public DateTime FechaActualizacion   { get; set; }
    public string   UsuarioActualizacion { get; set; } = string.Empty;
}

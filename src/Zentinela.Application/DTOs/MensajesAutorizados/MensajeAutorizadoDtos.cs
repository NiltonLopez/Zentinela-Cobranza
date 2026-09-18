namespace Zentinela.Application.DTOs.MensajesAutorizados;

public class MensajeAutorizadoDto
{
    public int      ID                   { get; set; }
    public string   Titulo               { get; set; } = string.Empty;
    public string   Texto                { get; set; } = string.Empty;
    public bool     Activo               { get; set; }
    /// <summary>Longitud del texto tal cual (con placeholders).</summary>
    public int      Longitud             { get; set; }
    /// <summary>Longitud sin placeholders: lo que ocupa el mensaje fijo.</summary>
    public int      LongitudBase         { get; set; }
    public DateTime FechaCreacion        { get; set; }
    public string   UsuarioCreacion      { get; set; } = string.Empty;
    public DateTime FechaActualizacion   { get; set; }
    public string   UsuarioActualizacion { get; set; } = string.Empty;
}

public class GuardarMensajeAutorizadoRequest
{
    public string Titulo { get; set; } = string.Empty;
    public string Texto  { get; set; } = string.Empty;
    public bool   Activo { get; set; } = true;
}

public class CambiarActivoRequest
{
    public bool Activo { get; set; }
}

/// <summary>Un carácter del texto que no cabe en un SMS GSM-7, con su posición (base 1).</summary>
public class CaracterRechazadoDto
{
    public string Caracter { get; set; } = string.Empty;
    public int    Posicion { get; set; }
}

/// <summary>Texto inválido para el catálogo. El controller la traduce a 400 con el detalle.</summary>
public sealed class MensajeAutorizadoInvalidoException(string mensaje, List<CaracterRechazadoDto>? rechazados = null)
    : Exception(mensaje)
{
    public List<CaracterRechazadoDto> CaracteresRechazados { get; } = rechazados ?? [];
}

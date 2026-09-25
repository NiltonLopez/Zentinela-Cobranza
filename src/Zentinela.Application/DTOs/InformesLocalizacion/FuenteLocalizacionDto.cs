namespace Zentinela.Application.DTOs.InformesLocalizacion;

// Los enums viajan como TEXTO (JsonStringEnumConverter en Program.cs): el front compara
// contra 'Funcional', no contra 0.

/// <summary>
/// Fuente de localización tal como la ve el front (<c>GET /api/fuentes-localizacion</c>, ADR-040).
/// Sale de <see cref="CatalogoFuentesLocalizacion"/>; el predicado SQL y la tabla no viajan.
/// </summary>
public class FuenteLocalizacionDto
{
    /// <summary>Clave del Cargue de Fuentes ("Fosyga", "DatosClaro"…).</summary>
    public string Clave { get; set; } = string.Empty;

    /// <summary>Nombre de la fila en "Porcentajes de Localización" del Calificador ("Claro", "ARL Sura"…).</summary>
    public string EtiquetaCalificador { get; set; } = string.Empty;

    /// <summary>Slug de la descarga por celda del Calificador (ADR-019).</summary>
    public string Slug { get; set; } = string.Empty;

    public EstadoFuente Estado { get; set; }
    public GrupoFuente Grupo { get; set; }

    /// <summary>Aclaración que acompaña al estado (o null).</summary>
    public string? Nota { get; set; }

    public bool EnPanelCalificador { get; set; }

    /// <summary>Fin del último <c>dbo.CargueFuenteEjecucion</c> con Estado 'OK' de la fuente; null si nunca se cargó por el módulo.</summary>
    public DateTime? UltimoCargueOk { get; set; }
}

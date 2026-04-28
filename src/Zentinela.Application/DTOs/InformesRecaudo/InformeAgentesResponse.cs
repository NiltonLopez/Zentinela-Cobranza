namespace Zentinela.Application.DTOs.InformesRecaudo;

/// <summary>
/// Respuesta del informe de agentes para un mes.
/// Incluye resumen global y el detalle por agente ordenado de menor a mayor recaudo.
/// </summary>
public class InformeAgentesResponse
{
    public decimal TotalRecaudo    { get; set; }
    public decimal TotalHonorarios { get; set; }
    public int     TotalAgentes    { get; set; }
    public int     TotalGestionado { get; set; }

    public List<InformeAgenteMesDto> Agentes { get; set; } = [];
}

/// <summary>Fila del informe de agentes.</summary>
public class InformeAgenteMesDto
{
    public long    AgenteID          { get; set; }
    public string  NombreAgente      { get; set; } = string.Empty;
    public int     LiderID           { get; set; }
    public string  NombreLider       { get; set; } = string.Empty;
    public string  NombreCartera     { get; set; } = string.Empty;

    /// <summary>Cuentas únicas (titulares) con pago en el mes.</summary>
    public int     Gestionado        { get; set; }
    public decimal Recaudo           { get; set; }
    public decimal Honorarios        { get; set; }

    /// <summary>Valor único de la meta asignada al agente para el mes.</summary>
    public decimal ValorMeta         { get; set; }

    /// <summary>"Recaudo" u "Honorarios" según el tipo de meta asignada. Null si no tiene meta.</summary>
    public string? TipoMeta          { get; set; }

    public bool    TieneMeta         { get; set; }

    /// <summary>ValorMeta proporcional a los días hábiles transcurridos del mes.</summary>
    public decimal DeberiaLlevarHoy  { get; set; }

    /// <summary>Máximo entre 0 y (ValorMeta − métrica correspondiente).</summary>
    public decimal FaltaParaMeta     { get; set; }

    /// <summary>Recaudo / días del mes con al menos un pago.</summary>
    public decimal PromedioPagoDia   { get; set; }
}

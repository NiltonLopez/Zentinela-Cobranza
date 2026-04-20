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

    public decimal Meta              { get; set; }
    public decimal MetaHonorarios    { get; set; }
    public bool    TieneMeta         { get; set; }

    /// <summary>MetaHonorarios proporcional a los días transcurridos del mes.</summary>
    public decimal DeberiaLlevarHoy  { get; set; }

    /// <summary>Máximo entre 0 y (MetaHonorarios − Honorarios).</summary>
    public decimal FaltaParaMeta     { get; set; }

    /// <summary>Recaudo / días del mes con al menos un pago.</summary>
    public decimal PromedioPagoDia   { get; set; }
}

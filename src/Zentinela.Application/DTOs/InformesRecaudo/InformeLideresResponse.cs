namespace Zentinela.Application.DTOs.InformesRecaudo;

/// <summary>
/// Respuesta del informe de líderes para un mes.
/// Una fila por combinación líder-cartera.
/// </summary>
public class InformeLideresResponse
{
    public decimal TotalRecaudo { get; set; }
    public decimal TotalMeta    { get; set; }
    public int     TotalLideres { get; set; }

    public List<InformeLiderCarteraDto> LiderCarteras { get; set; } = [];
}

/// <summary>Fila del informe de líderes: una entrada por cartera asignada al líder.</summary>
public class InformeLiderCarteraDto
{
    public int     LiderID          { get; set; }
    public string  NombreLider      { get; set; } = string.Empty;
    public int     CarteraID        { get; set; }
    public string  NombreCartera    { get; set; } = string.Empty;

    public decimal MetaCartera      { get; set; }
    public bool    TieneMeta        { get; set; }

    /// <summary>Titulares activos asignados a este líder en esta cartera.</summary>
    public int     Capacidad        { get; set; }

    public decimal Recaudo          { get; set; }
    public decimal Honorarios       { get; set; }

    /// <summary>MetaCartera × (días transcurridos / días del mes).</summary>
    public decimal DeberiaLlevarHoy { get; set; }

    /// <summary>(Recaudo / días transcurridos) × días del mes.</summary>
    public decimal ProyeccionCierre { get; set; }
}

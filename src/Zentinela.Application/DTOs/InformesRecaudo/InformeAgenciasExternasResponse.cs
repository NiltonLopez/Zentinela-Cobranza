namespace Zentinela.Application.DTOs.InformesRecaudo;

/// <summary>
/// Respuesta del informe administrativo de Agencias Externas.
/// Lista (líder × cartera externa) con recaudo, honorarios y total de asignaciones
/// del mes solicitado. Solo incluye relaciones donde Cartera.EsExterna = 1.
/// </summary>
public class InformeAgenciasExternasResponse
{
    public decimal TotalRecaudo      { get; set; }
    public decimal TotalHonorarios   { get; set; }
    public int     TotalAsignaciones { get; set; }
    /// <summary>Cantidad de líderes distintos con al menos una cartera externa.</summary>
    public int     TotalAgencias     { get; set; }

    public List<FilaAgenciaExternaDto> Filas { get; set; } = [];
}

/// <summary>Fila del informe de agencias externas: una entrada por (líder, cartera externa).</summary>
public class FilaAgenciaExternaDto
{
    public int     LiderID           { get; set; }
    public string  NombreLider       { get; set; } = string.Empty;
    public int     CarteraID         { get; set; }
    public string  NombreCartera     { get; set; } = string.Empty;

    public decimal Recaudo           { get; set; }
    public decimal Honorarios        { get; set; }
    /// <summary>Titulares activos asignados a este líder en esta cartera externa.</summary>
    public int     TotalAsignaciones { get; set; }
}

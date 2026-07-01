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
    /// <summary>Suma de las metas (ValorMeta) de las relaciones externas con meta.</summary>
    public decimal TotalMeta         { get; set; }
    /// <summary>Cantidad de líderes distintos con al menos una cartera externa.</summary>
    public int     TotalAgencias     { get; set; }

    public List<FilaAgenciaExternaDto> Filas { get; set; } = [];
}

/// <summary>
/// Fila del informe de agencias externas: una entrada por (líder, cartera externa).
/// Espeja las métricas del informe líder-cartera (ver <c>InformeLiderCarteraDto</c>).
/// </summary>
public class FilaAgenciaExternaDto
{
    public int     LiderID           { get; set; }
    public string  NombreLider       { get; set; } = string.Empty;
    public int     CarteraID         { get; set; }
    public string  NombreCartera     { get; set; } = string.Empty;

    /// <summary>Asesores activos (Cartera_Agente) de esta relación externa.</summary>
    public int     TotalAsesores     { get; set; }
    public decimal Recaudo           { get; set; }
    public decimal Honorarios        { get; set; }
    /// <summary>Titulares activos asignados a este líder en esta cartera externa (capacidad).</summary>
    public int     TotalAsignaciones { get; set; }

    // ── Métricas de meta (idénticas a la vista líder-cartera) ──
    public decimal ValorMeta         { get; set; }
    /// <summary>"Recaudo" | "Honorarios" | null (sin meta).</summary>
    public string? TipoMeta          { get; set; }
    public bool    TieneMeta         { get; set; }
    public decimal DeberiaLlevarHoy  { get; set; }
    public decimal ProyeccionCierre  { get; set; }
}

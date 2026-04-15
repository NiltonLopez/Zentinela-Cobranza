namespace Zentinela.Application.DTOs.InformesRecaudo;

/// <summary>
/// Respuesta del informe de carteras: comparativo entre dos meses.
/// Se usa tanto en "Mes pasado vs este mes" como en el comparativo libre.
/// </summary>
public class InformeCarterasResponse
{
    public PeriodoDto  PeriodoA { get; set; } = new();
    public PeriodoDto  PeriodoB { get; set; } = new();

    public decimal TotalRecaudoA    { get; set; }
    public decimal TotalRecaudoB    { get; set; }
    public decimal VariacionTotal   { get; set; }

    public List<InformeCarteraComparativoDto> Carteras { get; set; } = [];
}

/// <summary>Etiqueta legible de un período mes/año.</summary>
public class PeriodoDto
{
    public int    Año    { get; set; }
    public int    Mes    { get; set; }
    public string Nombre { get; set; } = string.Empty;  // ej. "Marzo 2026"
}

/// <summary>Fila de comparativo por cartera.</summary>
public class InformeCarteraComparativoDto
{
    public int     CarteraID          { get; set; }
    public string  NombreCartera      { get; set; } = string.Empty;

    public decimal RecaudoA           { get; set; }
    public decimal HonorariosA        { get; set; }
    public decimal RecaudoB           { get; set; }
    public decimal HonorariosB        { get; set; }

    /// <summary>RecaudoB − RecaudoA (positivo = creció).</summary>
    public decimal VariacionRecaudo   { get; set; }

    /// <summary>Variación porcentual respecto a RecaudoA. Null si RecaudoA = 0.</summary>
    public decimal? PorcentajeVariacion { get; set; }
}

namespace Zentinela.Application.DTOs.Ranking;

/// <summary>
/// Categoría de ranking de un agente para un año/mes dado, calculada en vivo
/// (sin persistencia) por ICategoriaRankingService. Incluye el desglose del
/// cálculo para trazabilidad (auditoría de por qué un agente quedó en tal categoría).
/// Regla vigente: ver CategoriaRankingService — pendiente por negocio: rangos/umbrales
/// y honorarios asociados a cada categoría.
/// </summary>
public class CategoriaAgenteDto
{
    public long   AgenteID     { get; set; }
    public string NombreAgente { get; set; } = string.Empty;

    /// <summary>Categoría final: "Diamante" | "Oro" | "Plata" | "Bronce".</summary>
    public string Categoria { get; set; } = string.Empty;

    /// <summary>Categoría que le correspondería solo por el promedio anual (antes del ajuste).</summary>
    public string CategoriaPorPromedio { get; set; } = string.Empty;

    public decimal  PromedioAnual         { get; set; }
    public decimal? HonorarioMesActual    { get; set; }
    public decimal? HonorarioMesAnterior  { get; set; }

    /// <summary>True si la categoría final se recalculó por bajo rendimiento vs. el mes anterior.</summary>
    public bool AjustePorBajoRendimiento { get; set; }
}

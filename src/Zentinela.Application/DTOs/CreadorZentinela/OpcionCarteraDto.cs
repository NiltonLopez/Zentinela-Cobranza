namespace Zentinela.Application.DTOs.CreadorZentinela;

/// <summary>
/// Entrada del selector de cartera en el módulo Creador Zentinela.
/// Una entrada por cada combinación (Cartera, Líder) en Lider_Cartera para
/// Director/Admin, más una entrada agregada por cartera con LiderID = null
/// que representa "todos los líderes de esta cartera".
/// Para el rol Líder, una sola entrada por cartera (su propio LiderID).
/// </summary>
public class OpcionCarteraDto
{
    public int    CarteraID { get; set; }

    /// <summary>
    /// Líder específico al que se restringe la consulta. null = entrada
    /// "Cartera Completo" — el TVF dbo.fn_BaseMadrePorCartera trata NULL
    /// como wildcard y devuelve todos los líderes de la cartera.
    /// </summary>
    public int?   LiderID   { get; set; }

    /// <summary>
    /// Texto que se muestra en el dropdown:
    ///   · "NombreCartera"               (rol Líder)
    ///   · "NombreCartera — NombreLíder" (Admin/Director, entrada específica)
    ///   · "NombreCartera Completo"      (Admin/Director, entrada agregada)
    /// </summary>
    public string Etiqueta  { get; set; } = "";
}

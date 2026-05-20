namespace Zentinela.Application.DTOs.Carteras;

public class CrearRelacionDto
{
    /// <summary>ID de una cartera existente. Null si se crea una cartera nueva.</summary>
    public int? CarteraID { get; set; }

    /// <summary>Nombre de la nueva cartera. Requerido si CarteraID es null.</summary>
    public string? NombreCartera { get; set; }

    public int LiderID { get; set; }

    /// <summary>
    /// Marca la relación líder-cartera como externa (Lider_Cartera.EsExterna = 1).
    /// Aplica siempre — tanto al crear una cartera nueva como al asociarse a una
    /// cartera existente — porque la misma cartera puede tener relaciones internas
    /// y externas en paralelo según el líder.
    /// </summary>
    public bool EsExterna { get; set; }
}

namespace Zentinela.Application.DTOs.Carteras;

public class RelacionLiderCarteraDto
{
    public int    LiderID           { get; set; }
    public int    CarteraID         { get; set; }
    public string NombreLider       { get; set; } = string.Empty;
    public string NombreCartera     { get; set; } = string.Empty;
    public int    TotalAsignaciones { get; set; }
    public int    TotalAsesores     { get; set; }
    /// <summary>Espejo de Cartera.EsExterna para que la UI marque la relación como externa.</summary>
    public bool   EsExterna         { get; set; }

    /// <summary>Relación activa (true) o retirada/inactivada (false). Ver ADR-022.</summary>
    public bool   Activo            { get; set; } = true;

    /// <summary>
    /// True si la relación tiene histórico que impide el borrado físico (pagos, metas,
    /// mensajería o IVR). La UI usa este flag para decidir la acción de la X:
    /// con histórico ⇒ Inactivar (soft); sin histórico ⇒ Eliminar (hard).
    /// </summary>
    public bool   TieneHistorico    { get; set; }
}

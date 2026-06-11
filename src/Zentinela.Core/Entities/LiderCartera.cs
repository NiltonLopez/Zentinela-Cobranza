namespace Zentinela.Core.Entities;

public class LiderCartera
{
    public int LiderID { get; set; }
    public Lider Lider { get; set; } = null!;
    public int CarteraID { get; set; }
    public Cartera Cartera { get; set; } = null!;

    /// <summary>
    /// Marca la relación como externa (operada por agencia/personal externo).
    /// La misma cartera puede tener varias relaciones — algunas internas y otras
    /// externas — porque distintos líderes pueden trabajarla en paralelo. Por eso
    /// el flag vive aquí y no en Cartera. Excluye la relación de informes, metas,
    /// dashboards admin y selector de Cargue de Pagos; la incluye en "Agencias Externas".
    /// </summary>
    public bool EsExterna { get; set; }

    /// <summary>
    /// Soft-delete de la relación. Una relación con histórico (pagos, metas, mensajería,
    /// IVR) NUNCA se borra físicamente porque rompería la trazabilidad del recaudo, que
    /// queda anclado al par (LiderID, CarteraID). En su lugar se "retira" marcando
    /// Activo = false: desaparece de lo OPERATIVO (generación de bases, selectores,
    /// dashboards, metas) pero SIGUE VISIBLE en lo HISTÓRICO (Informes de Recaudo, que
    /// NO filtra por Activo). Es reversible (Reactivar). Ortogonal a EsExterna.
    /// Ver ADR-022.
    /// </summary>
    public bool Activo { get; set; } = true;
}

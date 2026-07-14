namespace Zentinela.Core.Interfaces;

/// <summary>
/// Precisión del total devuelto por la preview del Creador (se evita el COUNT exacto
/// sobre la TVF, que es carísimo en carteras grandes):
/// - <see cref="Exacto"/>: se contaron todas las filas (resultado ≤ límite de preview).
/// - <see cref="Aproximado"/>: sin filtros y con muchas filas — el total viene de
///   Titular_Cartera, que puede diferir levemente del conteo real de la TVF.
/// - <see cref="Minimo"/>: con filtros y con muchas filas — solo se sabe que hay "más de N".
/// </summary>
public enum TipoTotalPreview
{
    Exacto,
    Aproximado,
    Minimo,
}

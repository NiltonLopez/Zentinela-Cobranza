namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Payload para confirmar el cargue de pagos validados.
/// Contiene el contexto (cartera + líder) y las filas a insertar.
/// </summary>
public class ConfirmarPagosRequest
{
    /// <summary>Cartera única del lote (modo estándar). Se ignora en modo masivo (cada fila trae la suya).</summary>
    public int                CarteraID    { get; set; }
    /// <summary>
    /// True = modo "Cargue Masivo": CarteraID y LiderID vienen resueltos por fila
    /// (columnas Cartera/Lider del archivo). False = modo estándar (CarteraID global).
    /// </summary>
    public bool               Masivo       { get; set; }
    public List<PagoFilaDto>  FilasValidas { get; set; } = [];
}

namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Payload para confirmar el cargue de pagos validados.
/// Contiene el contexto (cartera + líder) y las filas a insertar.
/// </summary>
public class ConfirmarPagosRequest
{
    public int                CarteraID    { get; set; }
    public List<PagoFilaDto>  FilasValidas { get; set; } = [];
}

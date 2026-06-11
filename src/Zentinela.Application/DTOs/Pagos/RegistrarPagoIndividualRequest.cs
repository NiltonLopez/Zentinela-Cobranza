namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Payload para registrar UN pago desde el formulario de "Pago individual".
/// Conceptualmente equivale a una fila del CSV del cargue masivo: la resolución de
/// LiderID y AgenteControlID corre por la misma lógica (ver CarguePagosService).
/// </summary>
public class RegistrarPagoIndividualRequest
{
    public int      CarteraID     { get; set; }
    public long     CedulaTitular { get; set; }
    public string   NombreCliente { get; set; } = string.Empty;
    public decimal  ValorPagado   { get; set; }
    public DateOnly FechaPago     { get; set; }
    public decimal  Honorarios    { get; set; }

    /// <summary>
    /// Usuario Control (ControlNext) del asesor seleccionado. Se ignora cuando
    /// <see cref="EsBaseGeneral"/> es true.
    /// </summary>
    public string?  UsuarioControl { get; set; }

    /// <summary>True = pago sin asesor responsable (BASE GENERAL); AgenteControlID queda NULL.</summary>
    public bool     EsBaseGeneral  { get; set; }
}

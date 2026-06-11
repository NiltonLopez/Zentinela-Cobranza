namespace Zentinela.Application.DTOs.Pagos;

/// <summary>Resultado de registrar un pago individual: incluye el líder resuelto para confirmación visual.</summary>
public class RegistrarPagoIndividualResponse
{
    public int     PagoID          { get; set; }
    public int     LiderID         { get; set; }
    public string  LiderNombre     { get; set; } = string.Empty;
    public long?   AgenteControlID { get; set; }
    public bool    EsBaseGeneral   { get; set; }
}

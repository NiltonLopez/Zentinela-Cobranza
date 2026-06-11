namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Error de negocio al registrar un pago individual (cédula no asignada, agente no encontrado,
/// ambigüedad de líder, pago duplicado). El controller lo traduce a 400 con el mensaje exacto;
/// cualquier otra excepción se deja burbujear.
/// </summary>
public class PagoIndividualException : Exception
{
    public PagoIndividualException(string mensaje) : base(mensaje) { }
}

namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Resultado de buscar un titular por cédula dentro de una cartera (para autocompletar el nombre
/// en el formulario de pago individual). Existe = false cuando la cédula no está asignada a la
/// cartera seleccionada — en ese caso el pago no podría resolver líder.
/// </summary>
public class TitularEnCarteraDto
{
    public bool    Existe { get; set; }
    public string? Nombre { get; set; }
}

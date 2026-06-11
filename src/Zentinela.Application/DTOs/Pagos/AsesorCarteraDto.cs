namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Asesor de una cartera para el autocomplete del formulario de pago individual.
/// El valor que se envía al registrar es <see cref="UsuarioControl"/> (Agente.UsuarioControlNext),
/// para que la resolución reutilice la misma lógica del cargue masivo.
/// </summary>
public class AsesorCarteraDto
{
    public long   AgenteID       { get; set; }
    public string Nombre         { get; set; } = string.Empty;
    public string UsuarioControl { get; set; } = string.Empty;
}

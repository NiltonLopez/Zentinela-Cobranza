namespace Zentinela.Application.DTOs.Pagos;

/// <summary>
/// Representa una fila parseada del CSV de pagos, con resultado de validación.
/// EsValida = true si CedulaTitular existe en la tabla Titular.
/// </summary>
public class PagoFilaDto
{
    public long     CedulaTitular { get; set; }
    public string   NombreCliente { get; set; } = string.Empty;
    public decimal  ValorPagado   { get; set; }
    public DateOnly FechaPago     { get; set; }
    public decimal  Honorarios       { get; set; }
    /// <summary>
    /// Valor crudo de la columna 6 del CSV — se usa como clave de búsqueda en Agente.UsuarioControlNext.
    /// El valor especial "BASE GENERAL" (case-insensitive) indica que el pago no tiene asesor
    /// responsable y se atribuye solo al líder.
    /// </summary>
    public string   UsuarioControl   { get; set; } = string.Empty;
    /// <summary>
    /// AgenteID resuelto por lookup Agente.UsuarioControlNext = UsuarioControl.
    /// NULL cuando UsuarioControl = "BASE GENERAL".
    /// </summary>
    public long?    AgenteControlID  { get; set; }
    /// <summary>LiderID resuelto automáticamente desde TitularCartera (o CarteraAgente como desempate) durante la validación.</summary>
    public int      LiderID          { get; set; }
    /// <summary>True si la fila es BASE GENERAL (sin asesor). UI puede usarla para etiquetar visualmente.</summary>
    public bool     EsBaseGeneral    { get; set; }
    /// <summary>
    /// True si el pago ya existe (misma cédula, fecha y valor en la cartera) o se repite
    /// dentro del mismo archivo. Las filas duplicadas son válidas pero NO se insertan:
    /// se reportan aparte para que el usuario sepa qué se omitió. ErrorMensaje contiene el motivo.
    /// </summary>
    public bool     EsDuplicado      { get; set; }
    /// <summary>
    /// True si la cédula no existe en la tabla maestra Titular: la fila es válida pero al
    /// confirmar se creará el titular (NumeroDocumento + Nombre). La UI la resalta en amarillo.
    /// </summary>
    public bool     TitularNuevo     { get; set; }
    public bool     EsValida         { get; set; }
    public string?  ErrorMensaje     { get; set; }
}

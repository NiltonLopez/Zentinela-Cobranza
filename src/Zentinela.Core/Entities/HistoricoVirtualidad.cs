namespace Zentinela.Core.Entities;

/// <summary>
/// Registro histórico de virtualidad (envío IVR / SMS / Correo) a un titular.
/// Una sola tabla con discriminador <see cref="Canal"/>, y dos formas de carga según el canal
/// (ver ADR-031):
///   - <b>IVR — carga única:</b> el archivo del marcador ya trae el resultado, así que la fila
///     se inserta completa con <see cref="Estado"/> ya traducido desde el código "Last App".
///   - <b>SMS / Correo — dos fases:</b> se inserta con <see cref="Estado"/> = null y una carga
///     posterior lo actualiza por PK.
/// <see cref="Estado"/> null significa "sin resultado conocido" — es el caso de las filas
/// cargadas por el flujo de dos fases que nunca recibieron su resultado.
/// El par (LiderID, CarteraID) se ancla a Lider_Cartera (ver ADR-014).
/// </summary>
public class HistoricoVirtualidad
{
    public long     ID              { get; set; }
    public string   Canal           { get; set; } = string.Empty; // IVR | SMS | Correo
    public long     NumeroDocumento { get; set; }
    public string   Destino         { get; set; } = string.Empty; // teléfono o email
    public int      CarteraID       { get; set; }
    public int      LiderID         { get; set; }
    /// <summary>Fecha del evento. En IVR es la "Fecha Marcado" del archivo del marcador.</summary>
    public DateOnly FechaEnvio      { get; set; }
    public string?  Estado          { get; set; }                 // null = sin resultado conocido
    /// <summary>
    /// Sello del lote de carga. El cargue masivo escribe el MISMO valor en todas las filas del
    /// lote (no el DEFAULT por fila), de modo que un cargue equivocado se puede revertir con
    /// un DELETE por este campo.
    /// </summary>
    public DateTime FechaCargue     { get; set; }
}

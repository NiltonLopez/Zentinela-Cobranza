using Zentinela.Application.DTOs.CargueFuentes;

namespace Zentinela.Infrastructure.SQLServer.Services.CargueFuentes;

/// <summary>
/// La operación choca con el estado actual: ya hay un cargue activo de esa fuente, se llegó
/// al tope de trabajos simultáneos, o el job no está en un estado que la admita.
/// El controller la traduce a 409 Conflict (misma convención que CargaIvrConflictoException).
/// </summary>
public class CargueFuentesConflictoException(string mensaje) : Exception(mensaje);

/// <summary>
/// El archivo no se puede cargar tal como viene (falta una columna obligatoria, XLSX por
/// encima del tope de filas, huella distinta a la validada). El job termina en Error y, si
/// se alcanzó a armar, conserva el preview para que el front muestre qué columnas encontró.
/// </summary>
internal sealed class CargueArchivoRechazadoException(string mensaje, PreviewCargueDto? preview = null)
    : Exception(mensaje)
{
    public PreviewCargueDto? Preview { get; } = preview;
}

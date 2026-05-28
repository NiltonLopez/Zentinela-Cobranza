using Zentinela.Application.DTOs.Pagos;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Servicio para el cargue de pagos confirmados desde CSV o XLSX.
/// Flujo de 2 pasos: validar (preview sin persistir) → confirmar (bulk insert).
/// </summary>
public interface ICarguePagosService
{
    /// <summary>
    /// Parsea el archivo (CSV o XLSX) y valida cada fila contra la BD.
    /// Por cada fila válida, resuelve el LiderID desde TitularCartera (CedulaTitular + CarteraID).
    /// No persiste nada — solo retorna el preview y las filas válidas para la UI.
    /// </summary>
    Task<ValidarPagosResponse> ValidarArchivoAsync(Stream archivo, int carteraID, string extension);

    /// <summary>
    /// Inserta en bloque las filas válidas en la tabla Pago.
    /// El LiderID de cada fila viene resuelto desde la validación.
    /// <paramref name="usuarioRegistro"/> es el UserName del usuario autenticado que confirma el cargue.
    /// Retorna el número de registros insertados.
    /// </summary>
    Task<int> ConfirmarCargueAsync(ConfirmarPagosRequest request, string usuarioRegistro);

    /// <summary>Genera la plantilla XLSX con las columnas requeridas para el cargue de pagos.</summary>
    byte[] GenerarPlantilla();

    /// <summary>Fecha del último pago registrado (FechaRegistro) para la cartera. Null si no hay pagos aún.</summary>
    Task<DateTime?> ObtenerUltimaActualizacionAsync(int carteraID);
}

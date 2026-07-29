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
    /// Marca como duplicadas las filas cuya combinación (cédula, fecha, valor) ya existe en la
    /// cartera o se repite en el archivo; estas se reportan aparte y no se insertan.
    /// No persiste nada — solo retorna el preview y las filas válidas para la UI.
    /// </summary>
    Task<ValidarPagosResponse> ValidarArchivoAsync(Stream archivo, int carteraID, string extension);

    /// <summary>
    /// Modo "Cargue Masivo": el archivo trae columnas Lider y Cartera obligatorias por fila.
    /// Resuelve CarteraID y LiderID por nombre exacto contra las relaciones Lider_Cartera activas
    /// (el par debe existir y estar activo). UsuarioControl es opcional (si viene, resuelve el asesor).
    /// No requiere carteraID global. No persiste nada.
    /// </summary>
    Task<ValidarPagosResponse> ValidarArchivoMasivoAsync(Stream archivo, string extension);

    /// <summary>
    /// Inserta en bloque las filas válidas en la tabla Pago.
    /// El LiderID de cada fila viene resuelto desde la validación.
    /// <paramref name="usuarioRegistro"/> es el UserName del usuario autenticado que confirma el cargue.
    /// Retorna el número de registros insertados.
    /// </summary>
    Task<int> ConfirmarCargueAsync(ConfirmarPagosRequest request, string usuarioRegistro);

    /// <summary>
    /// Registra UN pago desde el formulario individual, reutilizando la misma resolución de
    /// líder/agente y detección de duplicados del cargue masivo. Lanza
    /// <see cref="Zentinela.Application.DTOs.Pagos.PagoIndividualException"/> con el motivo si la
    /// fila no es válida o está duplicada. <paramref name="usuarioRegistro"/> = UserName autenticado.
    /// </summary>
    Task<RegistrarPagoIndividualResponse> RegistrarPagoIndividualAsync(
        RegistrarPagoIndividualRequest request, string usuarioRegistro);

    /// <summary>Asesores de una cartera (con UsuarioControlNext) para el autocomplete, filtrados por nombre.</summary>
    Task<List<AsesorCarteraDto>> BuscarAsesoresAsync(int carteraID, string? q);

    /// <summary>Busca un titular por cédula dentro de una cartera para autocompletar su nombre.</summary>
    Task<TitularEnCarteraDto> BuscarTitularAsync(int carteraID, long cedula);

    /// <summary>Genera la plantilla XLSX con las columnas requeridas para el cargue de pagos.</summary>
    byte[] GenerarPlantilla();

    /// <summary>Plantilla XLSX del modo "Cargue Masivo": incluye las columnas Lider y Cartera.</summary>
    byte[] GenerarPlantillaMasiva();

    /// <summary>Fecha del último pago registrado (FechaRegistro) para la cartera. Null si no hay pagos aún.</summary>
    Task<DateTime?> ObtenerUltimaActualizacionAsync(int carteraID);

    /// <summary>
    /// Todas las combinaciones (cartera, año, mes) para las que existe al menos un Pago —
    /// alimenta los selects dinámicos y mutuamente dependientes del tab "Eliminar pagos".
    /// </summary>
    Task<List<PeriodoPagoDto>> ObtenerPeriodosDisponiblesAsync();

    /// <summary>
    /// Vista previa (sin borrar) de cuántos pagos y por cuánto dinero hay en el período
    /// mensual dado. Filtra por Pago.FechaPago. carteraID null = todas las carteras.
    /// </summary>
    Task<ConteoPeriodoResponse> ContarPeriodoAsync(int anio, int mes, int? carteraID);

    /// <summary>
    /// Borra en bloque los pagos del período mensual dado (por Pago.FechaPago).
    /// carteraID null = todas las carteras. Operación irreversible — la UI debe mostrar
    /// el conteo de <see cref="ContarPeriodoAsync"/> y pedir confirmación antes de llamar esto.
    /// Devuelve la cantidad de filas borradas.
    /// </summary>
    Task<int> BorrarPeriodoAsync(int anio, int mes, int? carteraID);
}

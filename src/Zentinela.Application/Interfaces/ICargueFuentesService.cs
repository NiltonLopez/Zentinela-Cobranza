using Zentinela.Application.DTOs.CargueFuentes;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Cargue de Fuentes de Datos (Administración). Validar y confirmar corren en segundo plano:
/// los métodos que los inician devuelven de inmediato la foto del job y el front hace
/// polling con <see cref="Obtener"/>. Los conflictos (job activo de la misma fuente, estado
/// que no admite la operación) se señalan con una excepción que el controller traduce a 409.
/// </summary>
public interface ICargueFuentesService
{
    IReadOnlyList<FuenteCargueDto> ListarFuentes();

    /// <summary>Guarda el archivo y lanza la validación. <see cref="ArgumentException"/> = petición inválida.</summary>
    Task<CargueJobDto> IniciarValidacionAsync(
        string fuente, Stream archivo, string nombreArchivo, long bytes, string usuario, CancellationToken ct);

    /// <summary>null si el job no existe (o ya se purgó).</summary>
    CargueJobDto? Obtener(Guid jobId);

    /// <summary>Jobs en memoria + ejecuciones registradas en las últimas horas de retención.</summary>
    Task<IReadOnlyList<CargueJobResumenDto>> ListarAsync(CancellationToken ct);

    /// <summary>Registra la ejecución EN CURSO y lanza la carga. null si el job no existe.</summary>
    Task<CargueJobDto?> ConfirmarAsync(Guid jobId, string usuario, CancellationToken ct);

    /// <summary>null si el job no existe.</summary>
    CargueJobDto? Cancelar(Guid jobId, string usuario);

    /// <summary>Borra job y archivo. false si no existe.</summary>
    bool Descartar(Guid jobId);
}

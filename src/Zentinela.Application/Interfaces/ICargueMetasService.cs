using Zentinela.Application.DTOs.Metas;

namespace Zentinela.Application.Interfaces;

public interface ICargueMetasService
{
    /// <summary>
    /// Devuelve todas las combinaciones líder-cartera y agentes activos con su meta
    /// para el mes actual, más los datos informativos (asesores, cuentas asignadas).
    /// </summary>
    Task<EstadoMetasMesDto> ObtenerEstadoMesActualAsync();

    /// <summary>UPSERT de la meta para una combinación líder-cartera en el mes actual.</summary>
    Task<ResultadoMetaDto> GuardarMetaLiderAsync(GuardarMetaLiderRequest request);

    /// <summary>UPSERT de la meta para un agente en el mes actual.</summary>
    Task<ResultadoMetaDto> GuardarMetaAgenteAsync(GuardarMetaAgenteRequest request);
}

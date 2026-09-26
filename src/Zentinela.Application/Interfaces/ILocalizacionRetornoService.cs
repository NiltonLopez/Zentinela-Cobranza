using Zentinela.Application.DTOs.InformesLocalizacion;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Vínculo automático lote ↔ cargue de retorno (ADR-040, etapa 4): llena <c>LocalizacionEnvio.CargueEjecucionID</c>
/// y <c>FechaRetorno</c> de los lotes ENVIADOS cuando un cargue OK de su fuente trae titulares suyos, y los cierra
/// si la cobertura llega al umbral configurado (por defecto no cierra). Nunca lanza por falta de tablas: devuelve
/// <see cref="ResultadoVinculoRetorno.SinTablas"/>.
/// </summary>
public interface ILocalizacionRetornoService
{
    /// <summary>Tras un cargue OK (lo llama el motor del Cargue de Fuentes): solo esa ejecución.</summary>
    Task<ResultadoVinculoRetorno> VincularCargueAsync(long ejecucionID, CancellationToken ct = default);

    /// <summary>Puesta al día: todo lote ENVIADO sin vínculo contra los cargues OK de su fuente posteriores al envío.</summary>
    Task<ResultadoVinculoRetorno> VincularPendientesAsync(CancellationToken ct = default);
}

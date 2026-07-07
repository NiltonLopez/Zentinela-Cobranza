using Zentinela.Application.DTOs.AuditoriaGestiones;

namespace Zentinela.Application.Interfaces;

public interface IAuditoriaGestionesService
{
    /// <summary>
    /// Audita las carteras del filtro: consulta HistoricoEstadoTitular por
    /// FechaUltimaGestion, evalúa la congruencia perfil↔gestión con IA + reglas
    /// determinísticas, y devuelve el informe consolidado.
    /// </summary>
    Task<AnalizarAuditoriaResponse> AnalizarAsync(FiltroAuditoria filtro, CancellationToken ct = default);

    /// <summary>
    /// Estima el costo (COP) de evaluar con IA las gestiones del filtro, sin ejecutar
    /// el modelo: cuenta los tokens de entrada con :countTokens y estima los de salida.
    /// </summary>
    Task<EstimacionCostoResponse> EstimarCostoAsync(FiltroAuditoria filtro, CancellationToken ct = default);

    /// <summary>Lista los reportes ya generados y disponibles en memoria (compartidos, expiran a 48 h).</summary>
    IReadOnlyList<ReporteAuditoriaInfo> ListarReportes();
}

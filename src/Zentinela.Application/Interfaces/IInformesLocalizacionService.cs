using Zentinela.Application.DTOs.InformesLocalizacion;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Lecturas de los Informes de Localización (ADR-040) sobre el último corte OK del precálculo.
/// El alcance (<paramref name="liderID"/>, <paramref name="carteraID"/>) ya viene resuelto por el
/// controlador: para el rol Líder el liderID es siempre el suyo y la cartera es de su Lider_Cartera.
/// Lanzan <see cref="LocalizacionTablasFaltantesException"/> si falta el script y
/// <see cref="LocalizacionSinCorteException"/> si aún no hay corte.
/// </summary>
public interface IInformesLocalizacionService
{
    Task<EstadoLocalizacionDto> ObtenerEstadoAsync(CancellationToken ct = default);

    Task<ResumenLocalizacionDto> ObtenerResumenAsync(int? liderID, int? carteraID, CancellationToken ct = default);

    /// <param name="agrupar">par | cartera | lider.</param>
    Task<CoberturaLocalizacionDto> ObtenerCoberturaAsync(int? liderID, int? carteraID, string agrupar, CancellationToken ct = default);

    /// <param name="umbral">90 | 180 | 365 días.</param>
    Task<FrescuraLocalizacionDto> ObtenerFrescuraAsync(int? liderID, int? carteraID, int umbral, CancellationToken ct = default);

    /// <param name="liderPropio">LiderID del Líder que consulta (recibe top + su fila); null = Admin/Director.</param>
    Task<RankingsLocalizacionDto> ObtenerRankingsAsync(
        Utils.CriterioRankingLocalizacion criterio, bool incluirExternas, int? liderPropio, CancellationToken ct = default);

    // ── Etapa 4 ──

    /// <summary>
    /// KPIs del Resumen y por fuente en las últimas <paramref name="semanas"/> semanas: el último corte OK de cada
    /// semana, para el mismo universo del Resumen.
    /// </summary>
    Task<TendenciaLocalizacionDto> ObtenerTendenciaAsync(int? liderID, int? carteraID, int semanas, CancellationToken ct = default);

    /// <summary>Ranking de asesores (último gestor de cada titular) en el alcance, con foto.</summary>
    Task<AsesoresLocalizacionDto> ObtenerAsesoresAsync(
        int? liderID, int? carteraID, Utils.CriterioRankingAsesor criterio, CancellationToken ct = default);

    /// <summary>"Alertas de la semana" del alcance. Solo se muestran en la app: no se envían.</summary>
    Task<AlertasSemanaDto> ObtenerAlertasSemanaAsync(int? liderID, int? carteraID, CancellationToken ct = default);

    /// <summary>La cartera está en el Lider_Cartera ACTIVO del líder.</summary>
    Task<bool> EsCarteraDelLiderAsync(int liderID, int carteraID, CancellationToken ct = default);
}

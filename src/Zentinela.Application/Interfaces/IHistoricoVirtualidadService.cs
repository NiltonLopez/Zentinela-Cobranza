using Zentinela.Application.DTOs.Virtualidad.Historico;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Módulo "Gestión de Histórico de Virtualidad": carga masiva de solicitudes
/// IVR/SMS/Correo (Estado null) y actualización posterior de resultados.
/// </summary>
public interface IHistoricoVirtualidadService
{
    // Fase Solicitud
    byte[] GenerarPlantilla(string canal);
    Task<ValidarSolicitudResponse> ValidarAsync(Stream excelStream, string canal);
    Task<int> ConfirmarSolicitudAsync(List<FilaSolicitudDto> filas, string canal);

    // Fase Resultado
    byte[] GenerarPlantillaResultado(string canal);
    Task<byte[]> DescargarPendientesAsync(string canal, int? carteraID, DateOnly? desde, DateOnly? hasta);
    Task<ValidarResultadoResponse> ValidarResultadoAsync(Stream excelStream, string canal);
    Task<int> ConfirmarResultadoAsync(List<FilaResultadoDto> filas);

    /// <summary>Canales soportados y sus estados válidos (lista blanca por canal).</summary>
    IReadOnlyDictionary<string, string[]> EstadosPorCanal { get; }
}

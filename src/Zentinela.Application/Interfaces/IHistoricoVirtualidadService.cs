using Zentinela.Application.DTOs.Virtualidad.Historico;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Módulo "Gestión de Histórico de Virtualidad". Dos formas de carga según el canal (ADR-031):
///   - <b>IVR</b>: carga única — el archivo del marcador ya trae el resultado de cada llamada.
///   - <b>SMS / Correo</b>: dos fases — solicitud (Estado null) y resultado por PK.
/// </summary>
public interface IHistoricoVirtualidadService
{
    /// <summary>Modo de carga, estados y columnas esperadas del canal. Lo consume el frontend.</summary>
    CatalogoCanalDto ObtenerCatalogo(string canal);

    // ── Carga única — canal IVR ─────────────────────────────────────────────

    /// <summary>Parsea y valida sin persistir. Devuelve resumen, agregados y la huella del archivo.</summary>
    Task<ValidarCargaIvrResponse> ValidarCargaIvrAsync(Stream archivo, string nombreArchivo);

    /// <summary>
    /// Re-parsea el MISMO archivo (se exige que la huella coincida con la de la validación) e
    /// inserta las filas válidas en bloque. Lanza <c>CargaIvrConflictoException</c> si no coincide.
    /// </summary>
    Task<ConfirmarCargaIvrResponse> ConfirmarCargaIvrAsync(Stream archivo, string nombreArchivo, string huellaEsperada);

    // ── Dos fases — canales SMS y Correo ────────────────────────────────────

    byte[] GenerarPlantilla(string canal);
    Task<ValidarSolicitudResponse> ValidarAsync(Stream excelStream, string canal);
    Task<int> ConfirmarSolicitudAsync(List<FilaSolicitudDto> filas, string canal);
    Task<byte[]> DescargarPendientesAsync(string canal, int? carteraID, DateOnly? desde, DateOnly? hasta);
    Task<ValidarResultadoResponse> ValidarResultadoAsync(Stream excelStream, string canal);
    Task<int> ConfirmarResultadoAsync(List<FilaResultadoDto> filas);
}

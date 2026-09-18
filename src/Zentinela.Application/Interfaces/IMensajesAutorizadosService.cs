using Zentinela.Application.DTOs.MensajesAutorizados;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Catálogo cerrado de textos SMS. Sin borrado físico: un mensaje usado queda referenciado
/// por EstrategiaSms, así que solo se desactiva.
/// </summary>
public interface IMensajesAutorizadosService
{
    Task<List<MensajeAutorizadoDto>> ListarAsync(bool soloActivos, CancellationToken ct = default);
    Task<MensajeAutorizadoDto?>      ObtenerAsync(int id, CancellationToken ct = default);
    Task<MensajeAutorizadoDto>       CrearAsync(GuardarMensajeAutorizadoRequest request, string usuario, CancellationToken ct = default);
    Task<MensajeAutorizadoDto?>      ActualizarAsync(int id, GuardarMensajeAutorizadoRequest request, string usuario, CancellationToken ct = default);
    Task<bool>                       CambiarActivoAsync(int id, bool activo, string usuario, CancellationToken ct = default);

    /// <summary>
    /// Valida título, texto, placeholders y alfabeto GSM-7. Lanza
    /// <see cref="MensajeAutorizadoInvalidoException"/> con el detalle. Expuesta para que el
    /// formulario pueda validar sin guardar.
    /// </summary>
    void Validar(GuardarMensajeAutorizadoRequest request);
}

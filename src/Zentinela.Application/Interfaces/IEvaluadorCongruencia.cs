using Zentinela.Application.DTOs.AuditoriaGestiones;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Abstracción del evaluador de congruencia perfil ↔ gestión.
/// La capa Application no conoce el proveedor de IA (hoy Gemini).
/// </summary>
public interface IEvaluadorCongruencia
{
    /// <summary>
    /// Evalúa un lote de gestiones en una sola llamada al modelo y devuelve un
    /// veredicto por cada una (mapeado por <c>Indice</c>).
    /// </summary>
    Task<IReadOnlyList<VeredictoGestion>> EvaluarLoteAsync(
        IReadOnlyList<GestionParaEvaluar> lote, CancellationToken ct = default);

    /// <summary>
    /// Cuenta los tokens de entrada que consumiría evaluar un lote (sin ejecutar el modelo).
    /// Se usa para estimar el costo antes de auditar.
    /// </summary>
    Task<int> ContarTokensEntradaAsync(
        IReadOnlyList<GestionParaEvaluar> lote, CancellationToken ct = default);
}

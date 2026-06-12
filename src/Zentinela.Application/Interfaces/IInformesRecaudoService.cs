using Zentinela.Application.DTOs.InformesRecaudo;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Genera los 4 sub-informes del módulo de Recaudo:
///   1. Agentes  — recaudo, honorarios, meta y cumplimiento por agente (ordenado menor-mayor)
///   2. Líderes  — carteras, capacity, meta, proyección de cierre
///   3. Carteras — comparativo entre dos meses cualesquiera
/// </summary>
public interface IInformesRecaudoService
{
    /// <summary>
    /// Sub-informe 1: consolidado por agente para el mes indicado.
    /// Orden: menor a mayor recaudo.
    /// </summary>
    Task<InformeAgentesResponse> GetInformeAgentesAsync(int año, int mes);

    /// <summary>
    /// Sub-informe 2: una fila por combinación líder-cartera para el mes indicado.
    /// Incluye capacity (titulares asignados), meta, debería llevar hoy y proyección de cierre.
    /// </summary>
    Task<InformeLideresResponse> GetInformeLideresAsync(int año, int mes);

    /// <summary>
    /// Desglose especial de una cartera de "BASE GENERAL": una fila por líder que participó,
    /// atribuyendo cada pago al líder del asesor que lo registró (aunque ese líder no tenga la
    /// cartera asignada), más una fila agregada "BASE GENERAL" para los pagos sin asesor.
    /// Reutiliza el shape de InformeLideresResponse.
    /// </summary>
    Task<InformeLideresResponse> GetInformeCarteraBaseGeneralAsync(int carteraID, int año, int mes);

    /// <summary>
    /// Sub-informes 3 y 4: comparativo de recaudo y honorarios por cartera
    /// entre el período A y el período B (cualquier combinación mes/año).
    /// </summary>
    Task<InformeCarterasResponse> GetInformeCarterasAsync(int añoA, int mesA, int añoB, int mesB);

    /// <summary>
    /// Sub-informe de agencias externas: una fila por (líder, cartera externa)
    /// con recaudo, honorarios y total de asignaciones del mes.
    /// Solo incluye relaciones donde Cartera.EsExterna = 1 (complemento de los
    /// otros sub-informes que excluyen las externas del universo administrativo).
    /// </summary>
    Task<InformeAgenciasExternasResponse> GetInformeAgenciasExternasAsync(int año, int mes);
}

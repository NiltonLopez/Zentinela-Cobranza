using Zentinela.Application.DTOs.Virtualidad.IVR;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Informe de IVR (solo lectura). El registro de solicitudes/resultados IVR se
/// hace desde el módulo "Gestión de Solicitudes de Virtualidad" (Administración),
/// que persiste en la tabla unificada HistoricoVirtualidad (Canal = 'IVR').
/// </summary>
public interface IVirtualidadIVRService
{
    Task<InformeIVRDto> GetInformeAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);

    /// <summary>
    /// Análisis del canal: embudo y desenlaces sobre clientes únicos (ADR-031).
    /// Requiere resultados cargados (Estado no nulo); las filas del flujo viejo de dos
    /// fases quedan en el bucket "sin contacto" porque no tienen estado.
    /// </summary>
    Task<AnalisisIvrDto> GetAnalisisAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}

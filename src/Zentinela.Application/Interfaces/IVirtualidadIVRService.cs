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
}

using Zentinela.Application.DTOs.Virtualidad.Mensajeria;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Informe de Mensajería/SMS (solo lectura). El registro de solicitudes/resultados
/// se hace desde el módulo "Gestión de Solicitudes de Virtualidad" (Administración),
/// que persiste en la tabla unificada HistoricoVirtualidad (Canal = 'SMS').
/// </summary>
public interface IVirtualidadMensajeriaService
{
    Task<InformeMensajeriaDto> GetInformeAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}

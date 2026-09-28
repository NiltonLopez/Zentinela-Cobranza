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

    /// <summary>
    /// Excel del informe para que el líder trabaje sus estrategias: hoja "Resumen" (las
    /// mismas cifras del análisis) y hoja "Detalle por cliente" (una fila por cliente marcado,
    /// con su mejor desenlace, su estado de gestión y los pagos posteriores).
    /// </summary>
    Task<byte[]> ExportarExcelAsync(DateOnly desde, DateOnly hasta, int? liderID, int? carteraID);
}

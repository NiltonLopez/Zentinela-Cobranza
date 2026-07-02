namespace Zentinela.Application.DTOs.AuditoriaGestiones;

/// <summary>
/// Metadatos de un reporte de auditoría ya generado y disponible en memoria
/// (compartido entre usuarios). No incluye los datos, solo para listarlos.
/// </summary>
public record ReporteAuditoriaInfo(int CarteraID, DateOnly Dia, DateTime GeneradoUtc);

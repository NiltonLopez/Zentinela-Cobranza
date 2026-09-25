namespace Zentinela.Application.Interfaces;

/// <summary>
/// Miniaturas de las fotos de RRHH (<c>DBRecursosHumanos.dbo.FotoEmpleado</c>) de líderes y
/// asesores activos, para los avatares de los informes (ADR-040).
/// </summary>
public interface IFotoPersonaService
{
    /// <summary>
    /// Miniatura cuadrada en JPEG de la persona con esa cédula, o null si no tiene foto, si la
    /// foto no se puede decodificar o si el documento no es de un líder (<c>dbo.Lider</c>) ni de
    /// un asesor (<c>dbo.Agente</c>) activo: así el endpoint no sirve para recorrer RRHH.
    /// </summary>
    /// <param name="tam">Lado en píxeles: 48, 96, 160 o 320 (cualquier otro valor se toma como 160).</param>
    /// <returns>Los bytes del JPEG y su ETag (entre comillas, listo para el header).</returns>
    Task<(byte[] Jpeg, string ETag)?> ObtenerMiniaturaAsync(long documento, int tam, CancellationToken ct = default);
}

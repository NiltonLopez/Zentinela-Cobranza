using Zentinela.Application.DTOs.InformesGenerales;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Cruce masivo de una lista de teléfonos contra las fuentes de localización de
/// DBCallCenter, para acreditar "uso de base de datos" y atribuirlo por líder.
/// Es la versión masiva de <see cref="IRastreoLineasService"/> (que resuelve un número por vez).
/// </summary>
public interface IUsoBaseDatosService
{
    /// <summary>
    /// Parsea N archivos CSV/XLSX con una columna de teléfono, cruza los números
    /// distintos contra las 13 fuentes de localización y devuelve el uso agregado
    /// por fuente y por líder. NO persiste nada: el staging es una tabla #temp que
    /// muere al cerrar la conexión.
    /// </summary>
    Task<UsoBaseDatosDto> AnalizarAsync(
        IReadOnlyList<ArchivoTelefonos> archivos,
        CancellationToken ct = default);
}

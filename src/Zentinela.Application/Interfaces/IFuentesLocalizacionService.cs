using Zentinela.Application.DTOs.InformesLocalizacion;

namespace Zentinela.Application.Interfaces;

/// <summary>
/// Catálogo de fuentes de localización para el front (ADR-040): <see cref="CatalogoFuentesLocalizacion"/>
/// más la fecha del último cargue OK de cada fuente.
/// </summary>
public interface IFuentesLocalizacionService
{
    Task<IReadOnlyList<FuenteLocalizacionDto>> ListarAsync(CancellationToken ct = default);
}

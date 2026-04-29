using Zentinela.Application.DTOs.Consultas;

namespace Zentinela.Application.Interfaces;

public interface IRastreoLineasService
{
    Task<RastreoLineaDto> RastrearAsync(string numeroTelefono);
}

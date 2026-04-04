using Zentinela.Application.DTOs.Registro;

namespace Zentinela.Application.Interfaces;

public interface IRegistroService
{
    /// <summary>
    /// Registra un nuevo usuario validando su cédula contra RRHH,
    /// creando el Agente en DBCallCenter y el usuario Identity en DBZentinela.
    /// Lanza InvalidOperationException con el mensaje de error si el registro falla.
    /// </summary>
    Task<RegistroResponse> RegistrarAsync(RegistroRequest request);
}

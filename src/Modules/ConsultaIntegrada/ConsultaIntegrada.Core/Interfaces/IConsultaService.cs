using ConsultaIntegrada.Core.Entities;

namespace ConsultaIntegrada.Core.Interfaces
{
    public interface IConsultaService
    {
        Task<ConsultaResult> ConsultarPorCedulaAsync(string cedula);
        Task<bool> ValidarCedulaAsync(string cedula);
    }
}

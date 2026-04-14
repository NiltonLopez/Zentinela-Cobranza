using ConsultaIntegrada.Core.Entities;

namespace ConsultaIntegrada.Core.Interfaces
{
    public interface IConsultaRepository
    {
        Task<ConsultaResult?> ObtenerDatosPorCedulaAsync(string cedula);
    }
}

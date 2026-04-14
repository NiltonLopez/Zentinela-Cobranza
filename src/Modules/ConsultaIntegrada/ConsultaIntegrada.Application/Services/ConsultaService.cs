using ConsultaIntegrada.Core.Entities;
using ConsultaIntegrada.Core.Interfaces;

namespace ConsultaIntegrada.Application.Services;

public class ConsultaService : IConsultaService
{
    private readonly IConsultaRepository _repository;

    public ConsultaService(IConsultaRepository repository)
    {
        _repository = repository;
    }

    public async Task<ConsultaResult> ConsultarPorCedulaAsync(string cedula)
    {
        if (!await ValidarCedulaAsync(cedula))
        {
            return new ConsultaResult
            {
                Cedula = cedula,
                Encontrado = false,
                FechaConsulta = DateTime.Now
            };
        }

        var resultado = await _repository.ObtenerDatosPorCedulaAsync(cedula);

        if (resultado == null)
        {
            return new ConsultaResult
            {
                Cedula = cedula,
                Encontrado = false,
                FechaConsulta = DateTime.Now
            };
        }

        return resultado;
    }

    public async Task<bool> ValidarCedulaAsync(string cedula)
    {
        if (string.IsNullOrWhiteSpace(cedula))
            return false;

        if (cedula.Length < 6 || cedula.Length > 10)
            return false;

        if (!cedula.All(char.IsDigit))
            return false;

        return await Task.FromResult(true);
    }
}

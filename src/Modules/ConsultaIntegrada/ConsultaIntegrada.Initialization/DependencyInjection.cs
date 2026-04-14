using ConsultaIntegrada.Application.Services;
using ConsultaIntegrada.Core.Interfaces;
using ConsultaIntegrada.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace ConsultaIntegrada.Initialization;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IConsultaService, ConsultaService>();
        services.AddScoped<IConsultaRepository, ConsultaRepository>();

        return services;
    }
}

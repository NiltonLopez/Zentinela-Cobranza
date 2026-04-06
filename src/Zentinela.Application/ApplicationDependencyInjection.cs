using Microsoft.Extensions.DependencyInjection;
using Zentinela.Application.Interfaces;
using Zentinela.Application.Services;

namespace Zentinela.Application;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICarteraService, CarteraService>();
        services.AddScoped<ICreadorZentinelaService, CreadorZentinelaService>();
        return services;
    }
}

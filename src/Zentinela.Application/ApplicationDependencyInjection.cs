using Microsoft.Extensions.DependencyInjection;

namespace Zentinela.Application;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Los servicios de aplicación se registran aquí a medida que se agregan UseCases.
        // AuthService vive en Infrastructure.SQLServer (usa UserManager<ApplicationUser>).
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Zentinela.Application.Interfaces;

namespace Zentinela.Infrastructure.FileExporter;

public static class FileExporterDependencyInjection
{
    public static IServiceCollection AddFileExporterInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<IExcelExporter, ExcelExporter>();
        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Zentinela.Infrastructure.SQLServer.Persistence;

namespace Zentinela.API.HealthChecks;

/// <summary>
/// Readiness check: verifica que la app está arriba y puede conectar a DBCallCenter
/// (la base principal). Un deploy con binarios incompletos/desincronizados suele fallar
/// al arrancar o al tocar la BD — este check lo delata y el smoke test del workflow
/// marca el deploy en rojo en vez de dar un "success" engañoso.
/// </summary>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Scope propio: el DbContext es scoped y el health check se evalúa fuera de una request.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var puedeConectar = await db.Database.CanConnectAsync(cancellationToken);
            return puedeConectar
                ? HealthCheckResult.Healthy("API arriba y DBCallCenter accesible.")
                : HealthCheckResult.Unhealthy("No se pudo conectar a DBCallCenter.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Fallo al verificar la conexión a la BD.", ex);
        }
    }
}

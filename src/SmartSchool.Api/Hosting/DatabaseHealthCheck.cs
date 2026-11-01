using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Hosting;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database unavailable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.GetType().Name);
        }
    }
}

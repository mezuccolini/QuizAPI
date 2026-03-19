using Microsoft.Extensions.Diagnostics.HealthChecks;
using QuizAPI.Data;

namespace QuizAPI.Services;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly QuizDbContext _db;

    public DatabaseHealthCheck(QuizDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection succeeded.")
                : HealthCheckResult.Unhealthy("Database connection failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check threw an exception.", ex);
        }
    }
}

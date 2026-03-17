using EventProcessor.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventProcessor.HealthChecks;

/// <summary>
/// Health check for the SQL settings configuration provider.
/// Reflects the <see cref="SqlSettingsHealthStatus"/> that the provider updates
/// during its runtime polling loop — no additional database round-trip is made.
/// </summary>
public sealed class SqlSettingsHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public SqlSettingsHealthCheck(IConfiguration configuration) =>
        _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        var provider = ((IConfigurationRoot)_configuration).Providers
            .OfType<SqlSettingsConfigurationProvider>()
            .FirstOrDefault();

        if (provider is null)
            return Task.FromResult(HealthCheckResult.Degraded(
                "SQL settings provider is not registered. " +
                "Ensure AddSqlSettings() was called during configuration setup."));

        var result = provider.HealthStatus switch
        {
            SqlSettingsHealthStatus.Healthy =>
                HealthCheckResult.Healthy("SQL settings polling is healthy."),
            SqlSettingsHealthStatus.Starting =>
                HealthCheckResult.Degraded("SQL settings provider has not yet completed its first poll."),
            SqlSettingsHealthStatus.Degraded =>
                HealthCheckResult.Unhealthy(
                    "SQL settings provider last poll failed — application is running on stale configuration."),
            _ =>
                HealthCheckResult.Unhealthy($"Unknown health status: {provider.HealthStatus}")
        };

        return Task.FromResult(result);
    }
}

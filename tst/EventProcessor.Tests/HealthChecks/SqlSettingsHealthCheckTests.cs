using EventProcessor.Configuration;
using EventProcessor.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;

namespace EventProcessor.Tests.HealthChecks;

public sealed class SqlSettingsHealthCheckTests
{
    [Theory]
    [InlineData(SqlSettingsHealthStatus.Healthy,  HealthStatus.Healthy)]
    [InlineData(SqlSettingsHealthStatus.Starting, HealthStatus.Degraded)]
    [InlineData(SqlSettingsHealthStatus.Degraded, HealthStatus.Unhealthy)]
    public async Task Status_maps_to_correct_health_result(
        SqlSettingsHealthStatus providerStatus,
        HealthStatus expected)
    {
        var provider = MakeProvider(providerStatus);
        // FakeConfigRoot holds the provider without ever invoking Load()
        var config = new FakeConfigRoot(provider);
        var check = new SqlSettingsHealthCheck(config);
        var ctx = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("sql-settings", check, null, null)
        };

        var result = await check.CheckHealthAsync(ctx, default);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Returns_degraded_when_provider_not_registered()
    {
        var config = new ConfigurationBuilder().Build();
        var check = new SqlSettingsHealthCheck(config);
        var ctx = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("sql-settings", check, null, null)
        };

        var result = await check.CheckHealthAsync(ctx, default);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    /// <summary>Creates a provider with the given status without calling Load().</summary>
    private static SqlSettingsConfigurationProvider MakeProvider(SqlSettingsHealthStatus status)
    {
        var provider = new SqlSettingsConfigurationProvider(new SqlSettingsOptions
        {
            ConnectionString = "Server=test",
            Application = "Test",
            Instance = "test"
        });
        provider.HealthStatus = status;   // accessible via InternalsVisibleTo
        return provider;
    }

    /// <summary>
    /// Minimal <see cref="IConfigurationRoot"/> stub that exposes the given provider
    /// without ever calling <c>Load()</c> on it.
    /// </summary>
    private sealed class FakeConfigRoot(IConfigurationProvider provider) : IConfigurationRoot
    {
        public IEnumerable<IConfigurationProvider> Providers => [provider];
        public string? this[string key] { get => null; set { } }
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException("Stub only");
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public void Reload() { }
    }
}




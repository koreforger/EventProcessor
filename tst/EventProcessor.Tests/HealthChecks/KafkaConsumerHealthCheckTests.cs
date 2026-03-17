using EventProcessor.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventProcessor.Tests.HealthChecks;

public sealed class KafkaConsumerHealthCheckTests
{
    private static HealthCheckContext MakeContext()
    {
        var check = new KafkaConsumerHealthCheck(new KafkaConsumerState());
        return new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("kafka-consumer", check, null, null)
        };
    }

    [Fact]
    public async Task Starting_state_returns_degraded()
    {
        var state = new KafkaConsumerState(); // default = Starting
        var result = await new KafkaConsumerHealthCheck(state)
            .CheckHealthAsync(MakeContext(), default);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Running_state_returns_healthy()
    {
        var state = new KafkaConsumerState();
        state.ReportRunning();

        var result = await new KafkaConsumerHealthCheck(state)
            .CheckHealthAsync(MakeContext(), default);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Stopped_state_returns_degraded()
    {
        var state = new KafkaConsumerState();
        state.ReportRunning();
        state.ReportStopped();

        var result = await new KafkaConsumerHealthCheck(state)
            .CheckHealthAsync(MakeContext(), default);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Faulted_state_returns_unhealthy_with_detail()
    {
        var state = new KafkaConsumerState();
        state.ReportFaulted("Connection refused");

        var result = await new KafkaConsumerHealthCheck(state)
            .CheckHealthAsync(MakeContext(), default);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Connection refused", result.Description);
    }
}

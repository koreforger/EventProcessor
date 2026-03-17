using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventProcessor.HealthChecks;

/// <summary>
/// Health check for the Kafka consumer host.
/// Reads from the <see cref="KafkaConsumerState"/> singleton that
/// <c>TransactionConsumerWorker</c> keeps up to date — no Kafka I/O is performed here.
/// </summary>
public sealed class KafkaConsumerHealthCheck : IHealthCheck
{
    private readonly KafkaConsumerState _state;

    public KafkaConsumerHealthCheck(KafkaConsumerState state) => _state = state;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        var result = _state.Status switch
        {
            KafkaConsumerHealthStatus.Running =>
                HealthCheckResult.Healthy("Kafka consumer is running."),
            KafkaConsumerHealthStatus.Starting =>
                HealthCheckResult.Degraded("Kafka consumer is starting up."),
            KafkaConsumerHealthStatus.Stopped =>
                HealthCheckResult.Degraded("Kafka consumer has stopped."),
            KafkaConsumerHealthStatus.Faulted =>
                HealthCheckResult.Unhealthy(
                    $"Kafka consumer faulted: {_state.Detail ?? "no detail available"}"),
            _ =>
                HealthCheckResult.Unhealthy($"Unknown consumer state: {_state.Status}")
        };

        return Task.FromResult(result);
    }
}

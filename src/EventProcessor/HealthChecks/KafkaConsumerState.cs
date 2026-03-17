namespace EventProcessor.HealthChecks;

/// <summary>
/// Runtime health status of the Kafka consumer host.
/// </summary>
public enum KafkaConsumerHealthStatus
{
    /// <summary>Worker has not yet called StartAsync on the consumer host.</summary>
    Starting,

    /// <summary>Consumer host is running and consuming messages.</summary>
    Running,

    /// <summary>Consumer host faulted with an unrecoverable error.</summary>
    Faulted,

    /// <summary>Consumer host was stopped gracefully.</summary>
    Stopped,
}

/// <summary>
/// Shared singleton that bridges the gap between <c>TransactionConsumerWorker</c>
/// (which owns the consumer host) and <see cref="KafkaConsumerHealthCheck"/> (which
/// reads state on health check polls).
///
/// Thread-safety: status field is volatile; detail is written before status so that
/// a reader that observes Faulted will also observe the detail string.
/// </summary>
public sealed class KafkaConsumerState
{
    private volatile KafkaConsumerHealthStatus _status = KafkaConsumerHealthStatus.Starting;
    private volatile string? _detail;

    public KafkaConsumerHealthStatus Status => _status;
    public string? Detail => _detail;

    /// <summary>Called by the worker once the consumer host has started successfully.</summary>
    public void ReportRunning()
    {
        _detail = null;
        _status = KafkaConsumerHealthStatus.Running;
    }

    /// <summary>Called by the worker when an unrecoverable fault occurs.</summary>
    public void ReportFaulted(string detail)
    {
        _detail = detail;          // write detail first so readers see it with Faulted status
        _status = KafkaConsumerHealthStatus.Faulted;
    }

    /// <summary>Called by the worker during graceful shutdown.</summary>
    public void ReportStopped()
    {
        _detail = null;
        _status = KafkaConsumerHealthStatus.Stopped;
    }
}

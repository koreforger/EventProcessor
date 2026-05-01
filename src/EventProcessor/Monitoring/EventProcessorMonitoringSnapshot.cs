using Event.Streaming.Processing.Monitoring;

namespace EventProcessor.Monitoring;

/// <summary>
/// EventProcessor runtime monitoring snapshot returned by /api/monitoring/snapshot.
/// Implements IMonitoringSnapshot for the shared monitoring contract.
/// </summary>
public sealed class EventProcessorMonitoringSnapshot : IMonitoringSnapshot
{
    // IMonitoringSnapshot identity
    public string Application { get; set; } = "EventProcessor";
    public string Instance { get; set; } = "1";
    public string Environment { get; set; } = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Version { get; set; } = "1.0.0";
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;

    // IMonitoringSnapshot metrics
    public KafkaMetrics KafkaMetrics { get; set; } = new();
    public PipelineMetrics PipelineMetrics { get; set; } = new();
    public SettingsSyncMetrics SettingsSyncMetrics { get; set; } = new();
    public IReadOnlyDictionary<string, object> AppSpecificMetrics { get; set; }
        = new Dictionary<string, object>();

    // EventProcessor-specific fields
    public string SeekMode { get; set; } = "None";
    public bool StopBoundaryActive { get; set; }
    public long StopBoundaryMessagesSkipped { get; set; }
    public long ActiveSessionCount { get; set; }
    public long TotalDecisions { get; set; }
}

/// <summary>
/// Mutable accumulator for EventProcessor pipeline metrics.
/// Updated concurrently by batch processor workers.
/// </summary>
public sealed class EventProcessorMetricsAccumulator
{
    private long _processed;
    private long _errors;
    private long _stopBoundarySkipped;
    private long _decisions;

    public void RecordProcessed() => Interlocked.Increment(ref _processed);
    public void RecordError() => Interlocked.Increment(ref _errors);
    public void RecordStopBoundarySkip() => Interlocked.Increment(ref _stopBoundarySkipped);
    public void RecordDecision() => Interlocked.Increment(ref _decisions);

    public long TotalProcessed => Interlocked.Read(ref _processed);
    public long TotalErrors => Interlocked.Read(ref _errors);
    public long StopBoundarySkipped => Interlocked.Read(ref _stopBoundarySkipped);
    public long TotalDecisions => Interlocked.Read(ref _decisions);
}

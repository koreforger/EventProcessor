using EventProcessor.Hubs;
using KF.Metrics;
using KF.Time;

namespace EventProcessor.Services;

/// <summary>
/// Adapts the KoreForge <see cref="IMonitoringSnapshotProvider"/> to the
/// <see cref="MetricsSnapshot"/> contract expected by the SignalR dashboard.
/// All operation timing and counting is performed by the KoreForge metrics engine;
/// this class only translates the representation.
/// </summary>
public sealed class MetricsCollector
{
    private readonly IMonitoringSnapshotProvider _snapshotProvider;
    private readonly ISystemClock _clock;

    public MetricsCollector(IMonitoringSnapshotProvider snapshotProvider, ISystemClock clock)
    {
        _snapshotProvider = snapshotProvider;
        _clock = clock;
    }

    /// <summary>Returns a dashboard-ready snapshot of all recorded operations.</summary>
    public MetricsSnapshot GetSnapshot()
    {
        var monitoring = _snapshotProvider.GetSnapshot();

        var counters = new Dictionary<string, long>();
        var gauges = new Dictionary<string, double>();
        var rates = new Dictionary<string, double>();

        foreach (var op in monitoring.Operations)
        {
            counters[$"{op.Name}.total"]   = op.TotalCount;
            counters[$"{op.Name}.success"] = op.TotalCount - op.TotalFailures;
            counters[$"{op.Name}.failure"] = op.TotalFailures;
            gauges[$"{op.Name}.in_flight"] = op.CurrentInFlight;
            gauges[$"{op.Name}.avg_ms"]    = op.CurrentAverageDuration.TotalMilliseconds;
            gauges[$"{op.Name}.max_ms"]    = op.CurrentMaxDuration.TotalMilliseconds;
            rates[$"{op.Name}.rate"]       = op.CurrentRatePerSecond;
        }

        return new MetricsSnapshot
        {
            Timestamp = monitoring.GeneratedAt,
            Counters = counters,
            Gauges = gauges,
            Rates = rates
        };
    }
}


using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using EventProcessor.Hubs;

namespace EventProcessor.Services;

/// <summary>
/// Subscribes to the "EventProcessor" Meter via MeterListener and accumulates
/// counter/gauge values for on-demand snapshot retrieval.
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, long> _prevCounters = new();
    private bool _disposed;

    public MetricsCollector()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "EventProcessor")
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            _counters.AddOrUpdate(instrument.Name, value, (_, prev) => prev + value));

        _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
            _counters.AddOrUpdate(instrument.Name, value, (_, prev) => prev + value));

        _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            _gauges[instrument.Name] = value);

        _listener.Start();
    }

    /// <summary>
    /// Returns a snapshot of the current counter, gauge, and rate values.
    /// Rates are computed as delta-per-second since the last snapshot call.
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        var counters = new Dictionary<string, long>(_counters);
        var gauges = new Dictionary<string, double>(_gauges);
        var rates = new Dictionary<string, double>();

        foreach (var kvp in counters)
        {
            var prev = _prevCounters.GetValueOrDefault(kvp.Key, 0);
            var delta = kvp.Value - prev;
            rates[kvp.Key + ".rate"] = delta; // per snapshot interval (1s)
            _prevCounters[kvp.Key] = kvp.Value;
        }

        return new MetricsSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Counters = counters,
            Gauges = gauges,
            Rates = rates
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Dispose();
    }
}

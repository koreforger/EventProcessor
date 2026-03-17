using EventProcessor.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EventProcessor.Services;

/// <summary>
/// Background service that ticks every second and pushes a MetricsSnapshot
/// to all connected MetricsHub clients via SignalR.
/// </summary>
public sealed class MetricsBroadcaster : BackgroundService
{
    private readonly MetricsCollector _collector;
    private readonly IHubContext<MetricsHub> _hub;

    public MetricsBroadcaster(MetricsCollector collector, IHubContext<MetricsHub> hub)
    {
        _collector = collector;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var snapshot = _collector.GetSnapshot();
            await _hub.Clients.All.SendAsync("MetricsSnapshot", snapshot, stoppingToken);
        }
    }
}

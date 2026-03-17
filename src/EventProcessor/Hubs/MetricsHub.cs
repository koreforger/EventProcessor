using Microsoft.AspNetCore.SignalR;

namespace EventProcessor.Hubs;

/// <summary>
/// SignalR hub for streaming real-time metrics to connected dashboard clients.
/// Clients receive MetricsSnapshot pushes every second from MetricsBroadcaster.
/// </summary>
public sealed class MetricsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}

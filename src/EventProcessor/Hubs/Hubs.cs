using Microsoft.AspNetCore.SignalR;

namespace EventProcessor.Hubs;

/// <summary>
/// Pushes settings-change notifications to connected clients.
/// </summary>
public sealed class SettingsHub : Hub
{
    public static async Task BroadcastRefreshAsync(IHubContext<SettingsHub> context, string version)
        => await context.Clients.All.SendAsync("SettingsRefreshed", new { Version = version, RefreshedAt = DateTimeOffset.UtcNow });
}

/// <summary>
/// Pushes monitoring snapshots and incident notifications to connected clients.
/// </summary>
public sealed class MonitoringHub : Hub
{
    public static async Task BroadcastSnapshotAsync(IHubContext<MonitoringHub> context, object snapshot)
        => await context.Clients.All.SendAsync("MonitoringSnapshot", snapshot);

    public static async Task BroadcastIncidentAsync(IHubContext<MonitoringHub> context, object incident)
        => await context.Clients.All.SendAsync("NewIncident", incident);
}

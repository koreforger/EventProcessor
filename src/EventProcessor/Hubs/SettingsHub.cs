using Microsoft.AspNetCore.SignalR;

namespace EventProcessor.Hubs;

/// <summary>
/// SignalR hub for streaming settings change notifications to dashboard clients.
/// Pushes SettingsChanged events when the SqlSettingsConfigurationProvider detects a diff.
/// </summary>
public sealed class SettingsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}

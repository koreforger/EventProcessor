using EventProcessor.Configuration;
using EventProcessor.Hubs;
using EventProcessor.Logging;
using KF.Time;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Primitives;

namespace EventProcessor.Services;

/// <summary>
/// Reacts to configuration reload events triggered by the <see cref="SqlSettingsConfigurationProvider"/>
/// and pushes change summaries to connected SettingsHub clients via SignalR.
///
/// Responsibilities of this class:
///   1. Subscribe to <see cref="IConfiguration"/> change tokens (fired when DB poll detects a change).
///   2. Build a before/after diff of the current loaded settings.
///   3. Broadcast the diff to SignalR clients.
///
/// What this class does NOT do:
///   - Poll the database directly (that is the provider's job).
///   - Own the database connection.
/// </summary>
public sealed class SettingsMonitor : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IHubContext<SettingsHub> _hub;
    private readonly SqlSettingsConfigurationProvider? _provider;
    private readonly ISystemClock _clock;
    private readonly EventProcessorLog<SettingsMonitor> _log;

    public SettingsMonitor(
        IConfiguration configuration,
        IHubContext<SettingsHub> hub,
        ISystemClock clock,
        EventProcessorLog<SettingsMonitor> log)
    {
        _configuration = configuration;
        _hub = hub;
        _clock = clock;
        _log = log;

        // Locate the SQL provider in the running pipeline (null if not registered).
        if (configuration is IConfigurationRoot root)
            _provider = root.Providers.OfType<SqlSettingsConfigurationProvider>().FirstOrDefault();
    }

    /// <summary>
    /// Returns the current settings snapshot by reading the provider's loaded data and
    /// comparing each key with the active IConfiguration value.
    /// No database round-trip — uses the already-loaded provider cache.
    /// </summary>
    public List<SettingEntry> GetSettingsSnapshot()
    {
        var providerData = _provider?.GetLoadedData()
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        return providerData
            .Select(kv =>
            {
                var active = _configuration[kv.Key];
                return new SettingEntry
                {
                    Key = kv.Key,
                    DatabaseValue = kv.Value,
                    ActiveValue = active,
                    Status = active == kv.Value ? "synced" : "stale"
                };
            })
            .OrderBy(e => e.Key)
            .ToList();
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.Sql.Settings.Loaded.LogInformation("SettingsMonitor started");

        // Wait for IConfiguration change token fires (the provider calls OnReload()
        // when it detects a DB change, which fires the token).
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WaitForConfigurationChangeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var snapshot = GetSettingsSnapshot();

                _log.Sql.Settings.Changed.LogInformation(
                    "Settings change detected; broadcasting {Count} entries to dashboard",
                    snapshot.Count);

                await _hub.Clients.All.SendAsync("SettingsChanged", new SettingsChangedEvent
                {
                    Timestamp = _clock.UtcNow,
                    Changes = snapshot
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Sql.Settings.Error.LogError(ex, "Failed to broadcast settings change event");
                // Non-fatal: next configuration reload will trigger a new broadcast.
            }
        }

        _log.App.Config.Loaded.LogInformation("SettingsMonitor stopped");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task WaitForConfigurationChangeAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IChangeToken changeToken = ((IConfigurationRoot)_configuration).GetReloadToken();

        using var cancelReg = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(), tcs);
        using var changeReg = changeToken.RegisterChangeCallback(
            static state => ((TaskCompletionSource)state!).TrySetResult(), tcs);

        await tcs.Task;
    }
}


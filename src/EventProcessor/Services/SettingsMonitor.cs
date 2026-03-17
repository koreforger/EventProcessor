using EventProcessor.Configuration;
using EventProcessor.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;

namespace EventProcessor.Services;

/// <summary>
/// Monitors settings by comparing the database values against the active IConfiguration values.
/// Pushes change events to connected SettingsHub clients via SignalR.
/// Also provides a full snapshot for REST API initial load.
/// </summary>
public sealed class SettingsMonitor : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IHubContext<SettingsHub> _hub;
    private readonly SqlSettingsOptions _options;
    private Dictionary<string, string?> _lastDbSnapshot = new(StringComparer.OrdinalIgnoreCase);

    public SettingsMonitor(
        IConfiguration configuration,
        IHubContext<SettingsHub> hub,
        SqlSettingsOptions options)
    {
        _configuration = configuration;
        _hub = hub;
        _options = options;
    }

    /// <summary>
    /// Returns the full settings comparison: database value vs active configuration value.
    /// </summary>
    public List<SettingEntry> GetSettingsSnapshot()
    {
        var dbValues = LoadDatabaseSettings();
        var entries = new List<SettingEntry>();

        // All keys from DB
        foreach (var kvp in dbValues)
        {
            var activeValue = _configuration[kvp.Key];
            entries.Add(new SettingEntry
            {
                Key = kvp.Key,
                DatabaseValue = kvp.Value,
                ActiveValue = activeValue,
                Status = activeValue == kvp.Value ? "synced" : "stale"
            });
        }

        return entries.OrderBy(e => e.Key).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial snapshot
        _lastDbSnapshot = LoadDatabaseSettings();

        using var timer = new PeriodicTimer(_options.PollingInterval > TimeSpan.Zero
            ? _options.PollingInterval
            : TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var current = LoadDatabaseSettings();
                var changes = DetectChanges(_lastDbSnapshot, current);

                if (changes.Count > 0)
                {
                    await _hub.Clients.All.SendAsync("SettingsChanged", new SettingsChangedEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        Changes = changes
                    }, stoppingToken);
                }

                _lastDbSnapshot = current;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow — next tick will retry
            }
        }
    }

    private List<SettingEntry> DetectChanges(
        Dictionary<string, string?> previous,
        Dictionary<string, string?> current)
    {
        var changes = new List<SettingEntry>();

        foreach (var kvp in current)
        {
            if (!previous.TryGetValue(kvp.Key, out var oldVal) || oldVal != kvp.Value)
            {
                var activeValue = _configuration[kvp.Key];
                changes.Add(new SettingEntry
                {
                    Key = kvp.Key,
                    DatabaseValue = kvp.Value,
                    ActiveValue = activeValue,
                    Status = activeValue == kvp.Value ? "synced" : "stale"
                });
            }
        }

        // Detect removed keys
        foreach (var kvp in previous)
        {
            if (!current.ContainsKey(kvp.Key))
            {
                changes.Add(new SettingEntry
                {
                    Key = kvp.Key,
                    DatabaseValue = null,
                    ActiveValue = _configuration[kvp.Key],
                    Status = "missing"
                });
            }
        }

        return changes;
    }

    private Dictionary<string, string?> LoadDatabaseSettings()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_options.ConnectionString)) return data;

        using var connection = new SqlConnection(_options.ConnectionString);
        connection.Open();

        const string sql = """
            SELECT [Key], [Value]
            FROM dbo.Settings
            WHERE ApplicationId = @AppId
              AND (InstanceId IS NULL OR InstanceId = @InstanceId OR InstanceId = '*')
            ORDER BY
                CASE WHEN InstanceId IS NULL THEN 0 WHEN InstanceId = '*' THEN 1 ELSE 2 END
            """;

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@AppId", _options.Application);
        cmd.Parameters.AddWithValue("@InstanceId", _options.Instance);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            data[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return data;
    }
}

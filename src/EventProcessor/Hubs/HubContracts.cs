namespace EventProcessor.Hubs;

/// <summary>
/// A point-in-time snapshot of all application metrics.
/// </summary>
public sealed class MetricsSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, long> Counters { get; init; } = new();
    public Dictionary<string, double> Gauges { get; init; } = new();
    public Dictionary<string, double> Rates { get; init; } = new();
}

/// <summary>
/// Represents a single configuration setting with its database and active values.
/// </summary>
public sealed class SettingEntry
{
    public required string Key { get; init; }
    public string? DatabaseValue { get; init; }
    public string? ActiveValue { get; init; }

    /// <summary>"synced", "stale", or "missing"</summary>
    public required string Status { get; init; }

    public DateTime? LastModified { get; init; }
}

/// <summary>
/// Notification pushed when one or more settings change in the database.
/// </summary>
public sealed class SettingsChangedEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public List<SettingEntry> Changes { get; init; } = new();
}

namespace EventProcessor.Hubs;

/// <summary>
/// A point-in-time snapshot of all application metrics.
/// </summary>
public sealed class MetricsSnapshot
{
    /// <summary>Set by the caller; never defaulted here — use ISystemClock.</summary>
    public DateTimeOffset Timestamp { get; init; }
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

    public DateTimeOffset? LastModified { get; init; }
}

/// <summary>
/// Notification pushed when one or more settings change in the database.
/// </summary>
public sealed class SettingsChangedEvent
{
    /// <summary>Set by the caller; never defaulted here — use ISystemClock.</summary>
    public DateTimeOffset Timestamp { get; init; }
    public List<SettingEntry> Changes { get; init; } = new();
}


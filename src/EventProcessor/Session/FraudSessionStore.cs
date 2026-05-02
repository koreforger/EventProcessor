using KoreForge.Time;

namespace EventProcessor.Session;

/// <summary>
/// Represents a single fraud detection session, keyed by entity identifier (e.g., account ID).
/// Holds accumulated state: event count, risk signals, and the last activity timestamp.
/// </summary>
public sealed class FraudSession
{
    public string EntityId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }
    public int EventCount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<string> RiskSignals { get; } = [];
    public string? LastDecision { get; set; }
}

/// <summary>
/// Thread-safe, in-memory store for active FraudSessions.
/// Provides snapshot reads (lock-free) and idle-timeout eviction.
/// </summary>
public sealed class FraudSessionStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, FraudSession> _sessions = new();
    private readonly ISystemClock _clock;

    public FraudSessionStore(ISystemClock clock) => _clock = clock;

    public FraudSession GetOrCreate(string entityId)
    {
        var now = _clock.UtcNow;
        return _sessions.GetOrAdd(entityId, id => new FraudSession
        {
            EntityId = id,
            CreatedAt = now,
            LastActivityAt = now,
        });
    }

    public bool TryGet(string entityId, out FraudSession? session)
        => _sessions.TryGetValue(entityId, out session);

    public int ActiveCount => _sessions.Count;

    /// <summary>
    /// Removes sessions with no activity since <paramref name="idleThreshold"/>.
    /// Returns the count of evicted sessions.
    /// </summary>
    public int EvictIdle(DateTimeOffset idleThreshold)
    {
        var evicted = 0;
        foreach (var (key, session) in _sessions)
        {
            if (session.LastActivityAt < idleThreshold)
            {
                _sessions.TryRemove(key, out _);
                evicted++;
            }
        }
        return evicted;
    }

    public IReadOnlyCollection<FraudSession> Snapshot()
        => [.. _sessions.Values];
}

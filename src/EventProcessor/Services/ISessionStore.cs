using EventProcessor.Models;

namespace EventProcessor.Services;

/// <summary>
/// Abstraction for per-NID fraud session state backed by FASTER.
/// Supports async cache-through (loads from SQL on miss), checkpointing,
/// and idle eviction.
/// </summary>
public interface ISessionStore : IDisposable
{
    /// <summary>
    /// Returns the session for the given NID. If not in memory, attempts to load
    /// from the SQL repository (cache-through). Creates a new session only if no
    /// persisted data exists.
    /// </summary>
    ValueTask<FraudSession> GetOrCreateAsync(string nid);

    /// <summary>
    /// Writes the updated session back to the store and marks it dirty for flush.
    /// </summary>
    void Put(string nid, FraudSession session);

    /// <summary>
    /// Drains up to <paramref name="maxCount"/> dirty sessions for SQL persistence,
    /// returning the oldest-dirty entries first.
    /// </summary>
    IReadOnlyList<(string Nid, FraudSession Session)> DrainDirty(int maxCount);

    /// <summary>
    /// Removes sessions that have been idle longer than the configured timeout.
    /// Returns the number of sessions evicted.
    /// </summary>
    int EvictIdle(TimeSpan idleTimeout);

    /// <summary>
    /// Saves the FASTER state to disk so data survives process shutdown.
    /// </summary>
    Task CheckpointAsync(CancellationToken ct = default);

    /// <summary>
    /// Restores FASTER state from the last on-disk checkpoint (called at startup).
    /// </summary>
    Task RecoverAsync(CancellationToken ct = default);

    /// <summary>Total sessions in the store.</summary>
    long Count { get; }

    /// <summary>Sessions modified since last drain.</summary>
    long DirtyCount { get; }

    /// <summary>Number of routing buckets.</summary>
    int BucketCount { get; }

    /// <summary>Returns the bucket index for a given NID.</summary>
    int GetBucket(string nid);
}

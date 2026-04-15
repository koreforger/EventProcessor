using EventProcessor.Models;

namespace EventProcessor.Services;

/// <summary>
/// Abstraction for per-NID fraud session state backed by FASTER.
/// </summary>
public interface ISessionStore : IDisposable
{
    /// <summary>
    /// Returns the session for the given NID, creating a new one if it doesn't exist.
    /// </summary>
    FraudSession GetOrCreate(string nid);

    /// <summary>
    /// Writes the updated session back to the store and marks it dirty for flush.
    /// </summary>
    void Put(string nid, FraudSession session);

    /// <summary>
    /// Drains up to <paramref name="maxCount"/> dirty sessions for SQL persistence.
    /// </summary>
    IReadOnlyList<(string Nid, FraudSession Session)> DrainDirty(int maxCount);

    /// <summary>Total sessions in the store.</summary>
    long Count { get; }

    /// <summary>Sessions modified since last drain.</summary>
    long DirtyCount { get; }

    /// <summary>Number of routing buckets.</summary>
    int BucketCount { get; }

    /// <summary>Returns the bucket index for a given NID.</summary>
    int GetBucket(string nid);
}

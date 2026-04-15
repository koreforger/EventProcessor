using EventProcessor.Models;

namespace EventProcessor.Services;

/// <summary>
/// SQL-backed persistence layer for session slabs.
/// Each NID has a "current" slab and zero or more monthly archive slabs.
/// Sessions are stored as MessagePack-serialized binary blobs for fast load.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Loads the current slab plus up to <paramref name="lookbackDays"/> worth of
    /// archive slabs, merges them into a single <see cref="FraudSession"/>, and returns it.
    /// Returns <c>null</c> if no data exists for this NID.
    /// </summary>
    Task<FraudSession?> LoadAsync(string nid, int lookbackDays, CancellationToken ct = default);

    /// <summary>
    /// Saves the session as the "current" slab (upsert). If the current slab's earliest
    /// transaction is older than <paramref name="archiveAfterDays"/>, the oldest month
    /// is sliced off into an archive slab first.
    /// </summary>
    Task SaveAsync(string nid, FraudSession session, int archiveAfterDays, CancellationToken ct = default);

    /// <summary>
    /// Saves a batch of sessions. Each entry follows the same archive logic as <see cref="SaveAsync"/>.
    /// </summary>
    Task SaveBatchAsync(
        IReadOnlyList<(string Nid, FraudSession Session)> sessions,
        int archiveAfterDays,
        CancellationToken ct = default);
}

using EventProcessor.Logging;
using EventProcessor.Models;
using MessagePack;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace EventProcessor.Services;

/// <summary>
/// SQL-backed session repository using time-slabbed binary storage.
///
/// Table schema (auto-created if missing):
///   [FraudEngine].[SessionSlabs](
///     NID        nvarchar(64)  NOT NULL,
///     SlabKey    nvarchar(10)  NOT NULL,   -- "current" or "2025-12"
///     Data       varbinary(max) NOT NULL,  -- MessagePack blob
///     UpdatedAt  datetime2     NOT NULL,
///     PRIMARY KEY (NID, SlabKey)
///   )
/// </summary>
internal sealed class SqlSessionRepository : ISessionRepository
{
    private readonly IOptions<FraudEngineOptions> _options;
    private readonly EventProcessorLog<SqlSessionRepository> _log;

    public SqlSessionRepository(
        IOptions<FraudEngineOptions> options,
        EventProcessorLog<SqlSessionRepository> log)
    {
        _options = options;
        _log = log;
    }

    public async Task<FraudSession?> LoadAsync(string nid, int lookbackDays, CancellationToken ct = default)
    {
        var connectionString = _options.Value.SqlSink.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
            return null;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Load current + recent archive slabs within lookback window.
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = _options.Value.SqlSink.TimeoutSeconds;
        cmd.CommandText = """
            SELECT SlabKey, Data
            FROM [FraudEngine].[SessionSlabs]
            WHERE NID = @NID
              AND (SlabKey = 'current' OR UpdatedAt >= @CutoffDate)
            ORDER BY SlabKey DESC
            """;
        cmd.Parameters.AddWithValue("@NID", nid);
        cmd.Parameters.AddWithValue("@CutoffDate", DateTimeOffset.UtcNow.AddDays(-lookbackDays).UtcDateTime);

        var slabs = new List<FraudSession>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var data = (byte[])reader["Data"];
            var slab = MessagePackSerializer.Deserialize<FraudSession>(data);
            slabs.Add(slab);
        }

        if (slabs.Count == 0)
            return null;

        // Merge slabs: current slab is the base, archives contribute transactions only.
        var current = slabs[0]; // "current" sorts last alphabetically but we load it first by priority
        for (int i = 1; i < slabs.Count; i++)
        {
            current.Transactions.AddRange(slabs[i].Transactions);
            current.TransactionCount += slabs[i].TransactionCount;
            current.TotalAmount += slabs[i].TotalAmount;
        }

        // Sort all transactions by timestamp ascending after merge.
        current.Transactions.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return current;
    }

    public async Task SaveAsync(string nid, FraudSession session, int archiveAfterDays, CancellationToken ct = default)
    {
        var connectionString = _options.Value.SqlSink.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
            return;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Check if we need to archive the oldest month.
        await ArchiveIfNeededAsync(conn, nid, session, archiveAfterDays, ct);

        // Upsert the current slab.
        var data = MessagePackSerializer.Serialize(session);
        await UpsertSlabAsync(conn, nid, "current", data, ct);
    }

    public async Task SaveBatchAsync(
        IReadOnlyList<(string Nid, FraudSession Session)> sessions,
        int archiveAfterDays,
        CancellationToken ct = default)
    {
        var connectionString = _options.Value.SqlSink.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
            return;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var (nid, session) in sessions)
        {
            await ArchiveIfNeededAsync(conn, nid, session, archiveAfterDays, ct);
            var data = MessagePackSerializer.Serialize(session);
            await UpsertSlabAsync(conn, nid, "current", data, ct);
        }
    }

    private async Task ArchiveIfNeededAsync(
        SqlConnection conn, string nid, FraudSession session, int archiveAfterDays, CancellationToken ct)
    {
        if (session.EarliestTransactionAt is null || session.Transactions.Count == 0)
            return;

        var slabSpan = DateTimeOffset.UtcNow - session.EarliestTransactionAt.Value;
        if (slabSpan.TotalDays <= archiveAfterDays)
            return;

        // Find the oldest month boundary and slice off transactions before it.
        var cutoff = session.EarliestTransactionAt.Value.AddDays(archiveAfterDays > 30 ? 30 : archiveAfterDays);
        var toArchive = session.Transactions.Where(t => t.Timestamp < cutoff).ToList();
        if (toArchive.Count == 0)
            return;

        // Build the archive slab key from the oldest transaction's year-month.
        var oldest = toArchive.Min(t => t.Timestamp);
        var slabKey = oldest.ToString("yyyy-MM");

        // Create an archive slab containing just the sliced transactions.
        var archiveSlab = new FraudSession
        {
            NID = nid,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            LastActivityAt = toArchive.Max(t => t.Timestamp),
            TransactionCount = toArchive.Count,
            TotalAmount = toArchive.Sum(t => t.Amount),
            Transactions = toArchive,
            BaseCountry = session.BaseCountry,
            EarliestTransactionAt = oldest,
        };

        var archiveData = MessagePackSerializer.Serialize(archiveSlab);
        await UpsertSlabAsync(conn, nid, slabKey, archiveData, ct);

        // Remove archived transactions from the current session.
        session.Transactions.RemoveAll(t => t.Timestamp < cutoff);
        session.TransactionCount = session.Transactions.Count;
        session.TotalAmount = session.Transactions.Sum(t => t.Amount);
        session.EarliestTransactionAt = session.Transactions.Count > 0
            ? session.Transactions.Min(t => t.Timestamp)
            : null;

        _log.Session.Flush.Completed.LogInformation(
            "Archived {Count} transactions for NID {NID} to slab {SlabKey}",
            toArchive.Count, nid, slabKey);
    }

    private async Task UpsertSlabAsync(
        SqlConnection conn, string nid, string slabKey, byte[] data, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = _options.Value.SqlSink.TimeoutSeconds;
        cmd.CommandText = """
            MERGE [FraudEngine].[SessionSlabs] AS tgt
            USING (SELECT @NID AS NID, @SlabKey AS SlabKey) AS src
                ON tgt.NID = src.NID AND tgt.SlabKey = src.SlabKey
            WHEN MATCHED THEN UPDATE SET
                Data = @Data, UpdatedAt = @UpdatedAt
            WHEN NOT MATCHED THEN INSERT
                (NID, SlabKey, Data, UpdatedAt)
            VALUES (@NID, @SlabKey, @Data, @UpdatedAt);
            """;
        cmd.Parameters.AddWithValue("@NID", nid);
        cmd.Parameters.AddWithValue("@SlabKey", slabKey);
        cmd.Parameters.Add("@Data", System.Data.SqlDbType.VarBinary, -1).Value = data;
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

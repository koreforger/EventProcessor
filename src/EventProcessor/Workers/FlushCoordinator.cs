using EventProcessor.Logging;
using EventProcessor.Models;
using EventProcessor.Services;
using KF.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace EventProcessor.Workers;

/// <summary>
/// Background service that periodically drains dirty sessions from the
/// <see cref="ISessionStore"/> and persists them to SQL via the configured SqlSink.
/// </summary>
internal sealed class FlushCoordinator : BackgroundService
{
    private const string OpFlush = "session.flush";

    private readonly ISessionStore _store;
    private readonly IOptions<FraudEngineOptions> _options;
    private readonly EventProcessorLog<FlushCoordinator> _log;
    private readonly IOperationMonitor _monitor;

    public FlushCoordinator(
        ISessionStore store,
        IOptions<FraudEngineOptions> options,
        EventProcessorLog<FlushCoordinator> log,
        IOperationMonitor monitor)
    {
        _store = store;
        _options = options;
        _log = log;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushOpts = _options.Value.Flush;
        var interval = TimeSpan.FromMilliseconds(flushOpts.TimeBasedIntervalMs);
        var countThreshold = flushOpts.CountThreshold;

        _log.Session.Flush.Started.LogInformation(
            "Flush coordinator started — interval={IntervalMs}ms, countThreshold={Count}",
            flushOpts.TimeBasedIntervalMs, countThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_store.DirtyCount == 0)
                continue;

            using var scope = _monitor.Begin(OpFlush);
            try
            {
                var dirty = _store.DrainDirty(countThreshold);
                if (dirty.Count > 0)
                {
                    await FlushToSqlAsync(dirty, stoppingToken);
                    _log.Session.Flush.Completed.LogInformation(
                        "Flushed {Count} sessions to SQL (remaining dirty: {Remaining})",
                        dirty.Count, _store.DirtyCount);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                scope.MarkFailed();
                _log.Session.Flush.Error.LogError(ex,
                    "Flush cycle failed — {DirtyCount} sessions still dirty", _store.DirtyCount);
            }
        }
    }

    private async Task FlushToSqlAsync(
        IReadOnlyList<(string Nid, FraudSession Session)> sessions,
        CancellationToken ct)
    {
        var connectionString = _options.Value.SqlSink.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            _log.Session.Flush.Completed.LogDebug(
                "No SQL sink configured — {Count} sessions drained without persistence", sessions.Count);
            return;
        }

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Batch sessions into MERGE statements for upsert semantics.
        // Schema assumed: [FraudEngine].[Sessions](NID, Status, TransactionCount, TotalAmount,
        //   CurrentScore, BaseCountry, CreatedAt, LastActivityAt, DecisionType, DecidedAt)
        foreach (var (nid, session) in sessions)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = _options.Value.SqlSink.TimeoutSeconds;
            cmd.CommandText = """
                MERGE [FraudEngine].[Sessions] AS tgt
                USING (SELECT @NID AS NID) AS src ON tgt.NID = src.NID
                WHEN MATCHED THEN UPDATE SET
                    Status = @Status, TransactionCount = @TxCount, TotalAmount = @TotalAmount,
                    CurrentScore = @Score, BaseCountry = @Country, LastActivityAt = @LastActivity,
                    DecisionType = @DecisionType, DecidedAt = @DecidedAt
                WHEN NOT MATCHED THEN INSERT
                    (NID, Status, TransactionCount, TotalAmount, CurrentScore, BaseCountry,
                     CreatedAt, LastActivityAt, DecisionType, DecidedAt)
                VALUES (@NID, @Status, @TxCount, @TotalAmount, @Score, @Country,
                        @CreatedAt, @LastActivity, @DecisionType, @DecidedAt);
                """;

            cmd.Parameters.AddWithValue("@NID", nid);
            cmd.Parameters.AddWithValue("@Status", (int)session.Status);
            cmd.Parameters.AddWithValue("@TxCount", session.TransactionCount);
            cmd.Parameters.AddWithValue("@TotalAmount", session.TotalAmount);
            cmd.Parameters.AddWithValue("@Score", session.CurrentScore);
            cmd.Parameters.AddWithValue("@Country", (object?)session.BaseCountry ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", session.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("@LastActivity", session.LastActivityAt.UtcDateTime);
            cmd.Parameters.AddWithValue("@DecisionType", session.Decision != null ? (int)session.Decision.Decision : 0);
            cmd.Parameters.AddWithValue("@DecidedAt", session.Decision?.DecidedAt.UtcDateTime ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}

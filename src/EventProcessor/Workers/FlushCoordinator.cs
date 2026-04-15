using EventProcessor.Logging;
using EventProcessor.Services;
using KF.Metrics;
using Microsoft.Extensions.Options;

namespace EventProcessor.Workers;

/// <summary>
/// Background service that periodically:
///   1. Drains dirty sessions (oldest-first) and saves them to SQL via <see cref="ISessionRepository"/>
///   2. Evicts idle sessions from FASTER
///   3. Takes periodic FASTER checkpoints for crash recovery
/// On shutdown, performs a final flush + checkpoint to ensure no data is lost.
/// </summary>
internal sealed class FlushCoordinator : BackgroundService
{
    private const string OpFlush = "session.flush";
    private const string OpCheckpoint = "session.checkpoint";
    private const string OpEvict = "session.evict";

    private readonly ISessionStore _store;
    private readonly ISessionRepository _repository;
    private readonly IOptions<FraudEngineOptions> _options;
    private readonly EventProcessorLog<FlushCoordinator> _log;
    private readonly IOperationMonitor _monitor;
    private int _flushCyclesSinceCheckpoint;

    public FlushCoordinator(
        ISessionStore store,
        ISessionRepository repository,
        IOptions<FraudEngineOptions> options,
        EventProcessorLog<FlushCoordinator> log,
        IOperationMonitor monitor)
    {
        _store = store;
        _repository = repository;
        _options = options;
        _log = log;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var flushInterval = TimeSpan.FromMilliseconds(opts.Flush.TimeBasedIntervalMs);
        var countThreshold = opts.Flush.CountThreshold;
        var idleTimeout = TimeSpan.FromMinutes(opts.Sessions.IdleTimeoutMinutes);
        var archiveAfterDays = opts.Sessions.ArchiveAfterDays;
        var checkpointEveryNCycles = opts.Sessions.CheckpointEveryFlushCycles;

        // Recover from last checkpoint at startup.
        try
        {
            await _store.RecoverAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _log.Session.Flush.Error.LogError(ex, "Failed to recover FASTER checkpoint at startup");
        }

        _log.Session.Flush.Started.LogInformation(
            "Flush coordinator started — interval={IntervalMs}ms, countThreshold={Count}, idleTimeout={Idle}min, archiveAfter={Archive}d",
            opts.Flush.TimeBasedIntervalMs, countThreshold, opts.Sessions.IdleTimeoutMinutes, archiveAfterDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(flushInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // 1. Flush dirty sessions to SQL (oldest-first).
            if (_store.DirtyCount > 0)
            {
                using var scope = _monitor.Begin(OpFlush);
                try
                {
                    var dirty = _store.DrainDirty(countThreshold);
                    if (dirty.Count > 0)
                    {
                        await _repository.SaveBatchAsync(dirty, archiveAfterDays, stoppingToken);
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

            // 2. Evict idle sessions.
            using (_monitor.Begin(OpEvict))
            {
                _store.EvictIdle(idleTimeout);
            }

            // 3. Periodic checkpoint.
            _flushCyclesSinceCheckpoint++;
            if (_flushCyclesSinceCheckpoint >= checkpointEveryNCycles)
            {
                using (_monitor.Begin(OpCheckpoint))
                {
                    await _store.CheckpointAsync(stoppingToken);
                }
                _flushCyclesSinceCheckpoint = 0;
            }
        }

        // Shutdown: final flush + checkpoint to guarantee no data loss.
        await ShutdownFlushAsync();
    }

    private async Task ShutdownFlushAsync()
    {
        _log.Session.Flush.Started.LogInformation(
            "Shutdown flush: draining all {DirtyCount} dirty sessions", _store.DirtyCount);

        try
        {
            // Drain everything remaining.
            while (_store.DirtyCount > 0)
            {
                var dirty = _store.DrainDirty(_options.Value.Flush.CountThreshold);
                if (dirty.Count == 0) break;
                await _repository.SaveBatchAsync(dirty, _options.Value.Sessions.ArchiveAfterDays);
            }

            // Final checkpoint.
            await _store.CheckpointAsync();

            _log.Session.Flush.Completed.LogInformation("Shutdown flush and checkpoint completed");
        }
        catch (Exception ex)
        {
            _log.Session.Flush.Error.LogError(ex, "Shutdown flush failed — some data may be lost");
        }
    }
}

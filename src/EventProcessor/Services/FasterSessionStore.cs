using System.Collections.Concurrent;
using System.Text;
using EventProcessor.Logging;
using EventProcessor.Models;
using FASTER.core;
using MessagePack;
using Microsoft.Extensions.Options;

namespace EventProcessor.Services;

/// <summary>
/// FASTER-backed durable session store with:
///   - Checkpoint/recover: data survives process restarts via on-disk snapshots
///   - Cache-through: on miss, loads from SQL via <see cref="ISessionRepository"/>
///   - Oldest-dirty-first flushing: tracks a DirtyAt timestamp per entry
///   - Idle eviction: removes sessions inactive beyond the configured timeout
///   - Bucket-routed concurrency: each bucket has its own lock
/// </summary>
internal sealed class FasterSessionStore : ISessionStore
{
    private readonly FasterKV<string, byte[]> _store;
    private readonly object[] _bucketLocks;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dirtyMap = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastActivityMap = new();
    private readonly ISessionRepository _repository;
    private readonly EventProcessorLog<FasterSessionStore> _log;
    private readonly int _bucketCount;
    private readonly int _lookbackDays;
    private readonly string _checkpointDir;
    private long _count;

    public FasterSessionStore(
        IOptions<FraudEngineOptions> options,
        ISessionRepository repository,
        EventProcessorLog<FasterSessionStore> log)
    {
        _log = log;
        _repository = repository;
        _bucketCount = options.Value.Processing.BucketCount;
        _lookbackDays = options.Value.Sessions.LookbackDays;
        _checkpointDir = options.Value.Processing.CheckpointDirectory
            ?? Path.Combine(Path.GetTempPath(), "EventProcessor", "checkpoints");

        Directory.CreateDirectory(_checkpointDir);

        _store = new FasterKV<string, byte[]>(
            1L << 20,
            new LogSettings
            {
                LogDevice = Devices.CreateLogDevice(
                    Path.Combine(_checkpointDir, "hlog.log"), preallocateFile: false),
                ObjectLogDevice = Devices.CreateLogDevice(
                    Path.Combine(_checkpointDir, "hlog.obj.log"), preallocateFile: false),
                MemorySizeBits = 28, // 256 MB
                PageSizeBits = 22    // 4 MB pages
            },
            checkpointSettings: new CheckpointSettings
            {
                CheckpointDir = _checkpointDir,
            },
            serializerSettings: new SerializerSettings<string, byte[]>
            {
                keySerializer = () => new NidKeySerializer(),
                valueSerializer = () => new SessionValueSerializer()
            });

        _bucketLocks = new object[_bucketCount];
        for (int i = 0; i < _bucketCount; i++)
            _bucketLocks[i] = new object();

        _log.Session.Created.LogInformation(
            "FASTER session store initialized: {BucketCount} buckets, 256 MB memory, checkpoint={CheckpointDir}",
            _bucketCount, _checkpointDir);
    }

    public int BucketCount => _bucketCount;

    public int GetBucket(string nid) =>
        (nid.GetHashCode() & 0x7FFFFFFF) % _bucketCount;

    public async ValueTask<FraudSession> GetOrCreateAsync(string nid)
    {
        var bucket = GetBucket(nid);

        // Step 1: Check FASTER (fast path).
        lock (_bucketLocks[bucket])
        {
            using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
            byte[] output = default!;
            var status = session.Read(ref nid, ref output);

            if (status.Found && output != null)
            {
                _lastActivityMap[nid] = DateTimeOffset.UtcNow;
                return MessagePackSerializer.Deserialize<FraudSession>(output);
            }
        }

        // Step 2: Cache miss — load from SQL (cache-through).
        var loaded = await _repository.LoadAsync(nid, _lookbackDays);
        if (loaded != null)
        {
            lock (_bucketLocks[bucket])
            {
                using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
                var value = MessagePackSerializer.Serialize(loaded);
                session.Upsert(ref nid, ref value);
            }
            Interlocked.Increment(ref _count);
            _lastActivityMap[nid] = DateTimeOffset.UtcNow;

            _log.Session.Created.LogDebug(
                "Loaded session for NID {NID} from SQL ({TxCount} transactions)", nid, loaded.TransactionCount);
            return loaded;
        }

        // Step 3: No data anywhere — create brand new session.
        var newSession = new FraudSession
        {
            NID = nid,
            Status = SessionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
        };

        lock (_bucketLocks[bucket])
        {
            using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
            var value = MessagePackSerializer.Serialize(newSession);
            session.Upsert(ref nid, ref value);
        }
        Interlocked.Increment(ref _count);
        _lastActivityMap[nid] = DateTimeOffset.UtcNow;
        return newSession;
    }

    public void Put(string nid, FraudSession fraudSession)
    {
        var bucket = GetBucket(nid);
        lock (_bucketLocks[bucket])
        {
            using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
            var value = MessagePackSerializer.Serialize(fraudSession);
            session.Upsert(ref nid, ref value);
        }
        var now = DateTimeOffset.UtcNow;
        _dirtyMap[nid] = now;
        _lastActivityMap[nid] = now;
    }

    public IReadOnlyList<(string Nid, FraudSession Session)> DrainDirty(int maxCount)
    {
        // Oldest-dirty-first: sort by DirtyAt ascending, take the oldest entries.
        var oldest = _dirtyMap
            .OrderBy(kv => kv.Value)
            .Take(maxCount)
            .Select(kv => kv.Key)
            .ToList();

        var results = new List<(string, FraudSession)>();

        foreach (var nid in oldest)
        {
            if (!_dirtyMap.TryRemove(nid, out _))
                continue;

            var bucket = GetBucket(nid);
            lock (_bucketLocks[bucket])
            {
                using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
                var key = nid;
                byte[] output = default!;
                var status = session.Read(ref key, ref output);

                if (status.Found && output != null)
                    results.Add((nid, MessagePackSerializer.Deserialize<FraudSession>(output)));
            }
        }

        return results;
    }

    public int EvictIdle(TimeSpan idleTimeout)
    {
        var cutoff = DateTimeOffset.UtcNow - idleTimeout;
        var toEvict = _lastActivityMap
            .Where(kv => kv.Value < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        int evicted = 0;
        foreach (var nid in toEvict)
        {
            // Only evict if the session is not dirty (dirty sessions must be flushed first).
            if (_dirtyMap.ContainsKey(nid))
                continue;

            var bucket = GetBucket(nid);
            lock (_bucketLocks[bucket])
            {
                using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
                var key = nid;
                session.Delete(ref key);
            }

            _lastActivityMap.TryRemove(nid, out _);
            Interlocked.Decrement(ref _count);
            evicted++;
        }

        if (evicted > 0)
        {
            _log.Session.Created.LogInformation(
                "Evicted {Count} idle sessions (timeout={Timeout}min)", evicted, idleTimeout.TotalMinutes);
        }

        return evicted;
    }

    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        try
        {
            var (success, token) = await _store.TakeFullCheckpointAsync(CheckpointType.Snapshot, ct);
            if (success)
            {
                await _store.CompleteCheckpointAsync(ct);
                _log.Session.Flush.Completed.LogInformation(
                    "FASTER checkpoint completed: token={Token}", token);
            }
        }
        catch (Exception ex)
        {
            _log.Session.Flush.Error.LogError(ex, "FASTER checkpoint failed");
        }
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        try
        {
            _store.Recover();

            // Rebuild the in-memory tracking maps by scanning all entries.
            using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());
            var iter = session.Iterate();
            long count = 0;
            while (iter.GetNext(out var recordInfo))
            {
                var nid = iter.GetKey();
                _lastActivityMap[nid] = DateTimeOffset.UtcNow;
                count++;
            }
            Interlocked.Exchange(ref _count, count);

            _log.Session.Created.LogInformation(
                "FASTER recovered from checkpoint: {Count} sessions restored", count);
        }
        catch (FasterException)
        {
            // No checkpoint found — starting fresh.
            _log.Session.Created.LogInformation("No FASTER checkpoint found — starting with empty store");
        }
    }

    public long Count => Interlocked.Read(ref _count);
    public long DirtyCount => _dirtyMap.Count;

    public void Dispose() => _store.Dispose();

    // ── FASTER Serializers ──────────────────────────────────────────

    private sealed class NidKeySerializer : BinaryObjectSerializer<string>
    {
        public override void Serialize(ref string obj)
        {
            var bytes = Encoding.UTF8.GetBytes(obj);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public override void Deserialize(out string obj)
        {
            var len = reader.ReadInt32();
            obj = Encoding.UTF8.GetString(reader.ReadBytes(len));
        }
    }

    private sealed class SessionValueSerializer : BinaryObjectSerializer<byte[]>
    {
        public override void Serialize(ref byte[] obj)
        {
            writer.Write(obj.Length);
            writer.Write(obj);
        }

        public override void Deserialize(out byte[] obj)
        {
            var len = reader.ReadInt32();
            obj = reader.ReadBytes(len);
        }
    }
}

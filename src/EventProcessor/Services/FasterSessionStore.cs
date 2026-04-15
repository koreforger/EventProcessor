using System.Collections.Concurrent;
using System.Text;
using EventProcessor.Logging;
using EventProcessor.Models;
using FASTER.core;
using MessagePack;
using Microsoft.Extensions.Options;

namespace EventProcessor.Services;

/// <summary>
/// FASTER-backed in-memory session store with deterministic bucket routing.
/// Each bucket has its own lock for partitioned concurrent access.
/// Values are MessagePack-serialized <see cref="FraudSession"/> byte arrays.
/// Data is purely in-memory (NullDevice); the <see cref="Workers.FlushCoordinator"/>
/// drains dirty sessions to SQL before eviction.
/// </summary>
internal sealed class FasterSessionStore : ISessionStore
{
    private readonly FasterKV<string, byte[]> _store;
    private readonly object[] _bucketLocks;
    private readonly ConcurrentDictionary<string, byte> _dirtySet = new();
    private readonly EventProcessorLog<FasterSessionStore> _log;
    private readonly int _bucketCount;
    private long _count;

    public FasterSessionStore(
        IOptions<FraudEngineOptions> options,
        EventProcessorLog<FasterSessionStore> log)
    {
        _log = log;
        _bucketCount = options.Value.Processing.BucketCount;

        _store = new FasterKV<string, byte[]>(
            1L << 20,
            new LogSettings
            {
                LogDevice = new NullDevice(),
                ObjectLogDevice = new NullDevice(),
                MemorySizeBits = 28, // 256 MB
                PageSizeBits = 22    // 4 MB pages
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
            "FASTER session store initialized: {BucketCount} buckets, 256 MB memory", _bucketCount);
    }

    public int BucketCount => _bucketCount;

    public int GetBucket(string nid) =>
        (nid.GetHashCode() & 0x7FFFFFFF) % _bucketCount;

    public FraudSession GetOrCreate(string nid)
    {
        var bucket = GetBucket(nid);
        lock (_bucketLocks[bucket])
        {
            using var session = _store.NewSession(new SimpleFunctions<string, byte[]>());

            byte[] output = default!;
            var status = session.Read(ref nid, ref output);

            if (status.Found && output != null)
                return MessagePackSerializer.Deserialize<FraudSession>(output);

            var newSession = new FraudSession
            {
                NID = nid,
                Status = SessionStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActivityAt = DateTimeOffset.UtcNow,
            };

            var value = MessagePackSerializer.Serialize(newSession);
            session.Upsert(ref nid, ref value);
            Interlocked.Increment(ref _count);
            return newSession;
        }
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
        _dirtySet.TryAdd(nid, 0);
    }

    public IReadOnlyList<(string Nid, FraudSession Session)> DrainDirty(int maxCount)
    {
        var results = new List<(string, FraudSession)>();
        var keys = _dirtySet.Keys.Take(maxCount).ToList();

        foreach (var nid in keys)
        {
            if (!_dirtySet.TryRemove(nid, out _))
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

    public long Count => Interlocked.Read(ref _count);
    public long DirtyCount => _dirtySet.Count;

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

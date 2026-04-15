using EventProcessor.Models;
using EventProcessor.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EventProcessor.Tests;

public sealed class FasterSessionStoreTests : IDisposable
{
    private readonly FasterSessionStore _store;
    private readonly string _checkpointDir;

    public FasterSessionStoreTests()
    {
        _checkpointDir = Path.Combine(Path.GetTempPath(), "EventProcessor", "tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions
            {
                BucketCount = 8,
                CheckpointDirectory = _checkpointDir,
            },
            Sessions = new SessionOptions
            {
                LookbackDays = 180,
                IdleTimeoutMinutes = 40,
            },
        });
        var repository = new NullSessionRepository();
        _store = new FasterSessionStore(options, repository, TestLogHelper.CreateLog<FasterSessionStore>());
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_checkpointDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task GetOrCreateAsync_returns_new_session_for_unknown_nid()
    {
        var session = await _store.GetOrCreateAsync("NID-001");

        session.Should().NotBeNull();
        session.NID.Should().Be("NID-001");
        session.Status.Should().Be(SessionStatus.Active);
        session.TransactionCount.Should().Be(0);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_returns_existing_session_for_known_nid()
    {
        var first = await _store.GetOrCreateAsync("NID-001");
        first.TransactionCount = 5;
        _store.Put("NID-001", first);

        var second = await _store.GetOrCreateAsync("NID-001");

        second.TransactionCount.Should().Be(5);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public async Task Put_marks_session_dirty()
    {
        var session = await _store.GetOrCreateAsync("NID-001");
        _store.DirtyCount.Should().Be(0);

        _store.Put("NID-001", session);

        _store.DirtyCount.Should().Be(1);
    }

    [Fact]
    public void DrainDirty_returns_dirty_sessions_oldest_first()
    {
        _store.Put("NID-OLDER", new FraudSession { NID = "NID-OLDER", TransactionCount = 1 });
        Thread.Sleep(10); // Ensure different DirtyAt timestamps.
        _store.Put("NID-NEWER", new FraudSession { NID = "NID-NEWER", TransactionCount = 2 });

        var drained = _store.DrainDirty(10);

        drained.Should().HaveCount(2);
        drained[0].Nid.Should().Be("NID-OLDER");
        drained[1].Nid.Should().Be("NID-NEWER");
        _store.DirtyCount.Should().Be(0);
    }

    [Fact]
    public void DrainDirty_respects_max_count()
    {
        for (int i = 0; i < 5; i++)
            _store.Put($"NID-{i:D3}", new FraudSession { NID = $"NID-{i:D3}" });

        var drained = _store.DrainDirty(2);

        drained.Should().HaveCount(2);
        _store.DirtyCount.Should().Be(3);
    }

    [Fact]
    public void BucketCount_reflects_configured_value()
    {
        _store.BucketCount.Should().Be(8);
    }

    [Fact]
    public void GetBucket_is_deterministic()
    {
        var bucket1 = _store.GetBucket("NID-001");
        var bucket2 = _store.GetBucket("NID-001");

        bucket1.Should().Be(bucket2);
        bucket1.Should().BeInRange(0, 7);
    }

    [Fact]
    public void GetBucket_distributes_across_buckets()
    {
        var buckets = new HashSet<int>();
        for (int i = 0; i < 100; i++)
            buckets.Add(_store.GetBucket($"NID-{i:D4}"));

        buckets.Count.Should().BeGreaterThan(4);
    }

    [Fact]
    public async Task Multiple_sessions_tracked_independently()
    {
        var s1 = await _store.GetOrCreateAsync("NID-001");
        var s2 = await _store.GetOrCreateAsync("NID-002");

        s1.TransactionCount = 10;
        s2.TransactionCount = 20;
        _store.Put("NID-001", s1);
        _store.Put("NID-002", s2);

        var read1 = await _store.GetOrCreateAsync("NID-001");
        var read2 = await _store.GetOrCreateAsync("NID-002");

        read1.TransactionCount.Should().Be(10);
        read2.TransactionCount.Should().Be(20);
        _store.Count.Should().Be(2);
    }

    [Fact]
    public async Task Session_survives_put_with_all_fields()
    {
        var session = new FraudSession
        {
            NID = "NID-FULL",
            Status = SessionStatus.Flagged,
            TransactionCount = 42,
            TotalAmount = 99999.99m,
            CurrentScore = 0.85,
            BaseCountry = "US",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            LastActivityAt = DateTimeOffset.UtcNow,
            Transactions = { new TransactionRecord { TransactionId = "TX-1", Amount = 100m } },
        };

        _store.Put("NID-FULL", session);
        var read = await _store.GetOrCreateAsync("NID-FULL");

        read.Status.Should().Be(SessionStatus.Flagged);
        read.TransactionCount.Should().Be(42);
        read.TotalAmount.Should().Be(99999.99m);
        read.CurrentScore.Should().Be(0.85);
        read.BaseCountry.Should().Be("US");
        read.Transactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task EvictIdle_removes_stale_sessions()
    {
        var session = await _store.GetOrCreateAsync("NID-STALE");
        _store.Put("NID-STALE", session);

        // Drain dirty first so eviction is allowed.
        _store.DrainDirty(100);

        // Evict with a zero timeout — everything is "idle".
        var evicted = _store.EvictIdle(TimeSpan.Zero);

        evicted.Should().Be(1);
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task EvictIdle_skips_dirty_sessions()
    {
        var session = await _store.GetOrCreateAsync("NID-DIRTY");
        _store.Put("NID-DIRTY", session);

        // Do NOT drain dirty — eviction should skip it.
        var evicted = _store.EvictIdle(TimeSpan.Zero);

        evicted.Should().Be(0);
        _store.Count.Should().Be(1);
    }

    [Fact]    public void DrainDirty_with_zero_max_returns_empty()
    {
        _store.Put("NID-A", new FraudSession { NID = "NID-A" });

        var drained = _store.DrainDirty(0);

        drained.Should().BeEmpty();
        _store.DirtyCount.Should().Be(1); // still dirty
    }

    [Fact]
    public async Task RecoverAsync_with_no_checkpoint_starts_fresh()
    {
        // Point to an empty directory — no checkpoint exists.
        var emptyDir = Path.Combine(_checkpointDir, "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);

        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions
            {
                BucketCount = 4,
                CheckpointDirectory = emptyDir,
            },
            Sessions = new SessionOptions { LookbackDays = 180 },
        });
        using var store = new FasterSessionStore(
            options, new NullSessionRepository(), TestLogHelper.CreateLog<FasterSessionStore>());

        // Should not throw — gracefully starts fresh.
        await store.RecoverAsync();

        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task EvictIdle_with_long_timeout_evicts_nothing()
    {
        await _store.GetOrCreateAsync("NID-RECENT");

        var evicted = _store.EvictIdle(TimeSpan.FromHours(24));

        evicted.Should().Be(0);
        _store.Count.Should().Be(1);
    }

    [Fact]    public async Task Checkpoint_and_recover_restores_sessions()
    {
        var session = new FraudSession
        {
            NID = "NID-PERSIST",
            TransactionCount = 7,
            TotalAmount = 5000m,
            BaseCountry = "NO",
        };
        _store.Put("NID-PERSIST", session);

        await _store.CheckpointAsync();

        // Create a second store pointing to the same checkpoint directory.
        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions
            {
                BucketCount = 8,
                CheckpointDirectory = _checkpointDir,
            },
            Sessions = new SessionOptions { LookbackDays = 180 },
        });
        using var store2 = new FasterSessionStore(
            options, new NullSessionRepository(), TestLogHelper.CreateLog<FasterSessionStore>());
        await store2.RecoverAsync();

        store2.Count.Should().BeGreaterThan(0);
        var recovered = await store2.GetOrCreateAsync("NID-PERSIST");
        recovered.TransactionCount.Should().Be(7);
        recovered.BaseCountry.Should().Be("NO");
    }

    [Fact]
    public async Task Cache_through_loads_from_repository_on_miss()
    {
        // Set up a repository that returns a pre-existing session.
        var preExisting = new FraudSession
        {
            NID = "NID-FROM-SQL",
            TransactionCount = 42,
            TotalAmount = 99000m,
            BaseCountry = "SE",
            Status = SessionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        var repo = new FakeSessionRepository(preExisting);

        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions
            {
                BucketCount = 4,
                CheckpointDirectory = Path.Combine(_checkpointDir, "ct"),
            },
            Sessions = new SessionOptions { LookbackDays = 180 },
        });
        using var store = new FasterSessionStore(options, repo, TestLogHelper.CreateLog<FasterSessionStore>());

        var loaded = await store.GetOrCreateAsync("NID-FROM-SQL");

        loaded.TransactionCount.Should().Be(42);
        loaded.TotalAmount.Should().Be(99000m);
        loaded.BaseCountry.Should().Be("SE");
        store.Count.Should().Be(1);
    }

    // ── Test doubles ──────────────────────────────────────────────

    private sealed class NullSessionRepository : ISessionRepository
    {
        public Task<FraudSession?> LoadAsync(string nid, int lookbackDays, CancellationToken ct = default)
            => Task.FromResult<FraudSession?>(null);

        public Task SaveAsync(string nid, FraudSession session, int archiveAfterDays, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveBatchAsync(IReadOnlyList<(string Nid, FraudSession Session)> sessions, int archiveAfterDays, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSessionRepository(FraudSession session) : ISessionRepository
    {
        public Task<FraudSession?> LoadAsync(string nid, int lookbackDays, CancellationToken ct = default)
            => nid == session.NID ? Task.FromResult<FraudSession?>(session) : Task.FromResult<FraudSession?>(null);

        public Task SaveAsync(string nid, FraudSession s, int archiveAfterDays, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveBatchAsync(IReadOnlyList<(string Nid, FraudSession Session)> sessions, int archiveAfterDays, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

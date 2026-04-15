using EventProcessor.Models;
using EventProcessor.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EventProcessor.Tests;

public sealed class FasterSessionStoreTests : IDisposable
{
    private readonly FasterSessionStore _store;

    public FasterSessionStoreTests()
    {
        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions { BucketCount = 8 },
        });
        // EventProcessorLog<T> requires the KoreForge source-generated logger infrastructure.
        // We use the test helper to create a minimal instance.
        _store = new FasterSessionStore(options, TestLogHelper.CreateLog<FasterSessionStore>());
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public void GetOrCreate_returns_new_session_for_unknown_nid()
    {
        var session = _store.GetOrCreate("NID-001");

        session.Should().NotBeNull();
        session.NID.Should().Be("NID-001");
        session.Status.Should().Be(SessionStatus.Active);
        session.TransactionCount.Should().Be(0);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_returns_existing_session_for_known_nid()
    {
        var first = _store.GetOrCreate("NID-001");
        first.TransactionCount = 5;
        _store.Put("NID-001", first);

        var second = _store.GetOrCreate("NID-001");

        second.TransactionCount.Should().Be(5);
        _store.Count.Should().Be(1);
    }

    [Fact]
    public void Put_marks_session_dirty()
    {
        var session = _store.GetOrCreate("NID-001");
        _store.DirtyCount.Should().Be(0);

        _store.Put("NID-001", session);

        _store.DirtyCount.Should().Be(1);
    }

    [Fact]
    public void DrainDirty_returns_dirty_sessions_and_removes_from_set()
    {
        _store.Put("NID-001", new FraudSession { NID = "NID-001", TransactionCount = 1 });
        _store.Put("NID-002", new FraudSession { NID = "NID-002", TransactionCount = 2 });

        var drained = _store.DrainDirty(10);

        drained.Should().HaveCount(2);
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

        // With 8 buckets and 100 different NIDs, we should hit most buckets.
        buckets.Count.Should().BeGreaterThan(4);
    }

    [Fact]
    public void Multiple_sessions_tracked_independently()
    {
        var s1 = _store.GetOrCreate("NID-001");
        var s2 = _store.GetOrCreate("NID-002");

        s1.TransactionCount = 10;
        s2.TransactionCount = 20;
        _store.Put("NID-001", s1);
        _store.Put("NID-002", s2);

        var read1 = _store.GetOrCreate("NID-001");
        var read2 = _store.GetOrCreate("NID-002");

        read1.TransactionCount.Should().Be(10);
        read2.TransactionCount.Should().Be(20);
        _store.Count.Should().Be(2);
    }

    [Fact]
    public void Session_survives_put_with_all_fields()
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
            RecentTransactions = { new TransactionRecord { TransactionId = "TX-1", Amount = 100m } },
        };

        _store.Put("NID-FULL", session);
        var read = _store.GetOrCreate("NID-FULL");

        read.Status.Should().Be(SessionStatus.Flagged);
        read.TransactionCount.Should().Be(42);
        read.TotalAmount.Should().Be(99999.99m);
        read.CurrentScore.Should().Be(0.85);
        read.BaseCountry.Should().Be("US");
        read.RecentTransactions.Should().HaveCount(1);
    }
}

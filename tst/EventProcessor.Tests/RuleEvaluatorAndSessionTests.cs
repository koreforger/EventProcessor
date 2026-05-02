using EventProcessor.Rules;
using EventProcessor.Session;
using KoreForge.Time;

namespace EventProcessor.Tests;

public sealed class RuleEvaluatorTests
{
    private static FraudSession MakeSession(string id, int count = 0, decimal amount = 0m) =>
        new FraudSession { EntityId = id, CreatedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow, EventCount = count, TotalAmount = amount };

    [Fact]
    public void Evaluate_WithNoRules_ReturnsApproved()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([]);
        var result = evaluator.Evaluate(MakeSession("acc1"));
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void Evaluate_HighFrequency_Flagged()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([new HighFrequencyRule(threshold: 5)]);
        var session = MakeSession("acc1", count: 6);
        var result = evaluator.Evaluate(session);
        Assert.Equal(DecisionOutcome.Flagged, result.Outcome);
        Assert.Single(result.Signals);
    }

    [Fact]
    public void Evaluate_HighAmount_Flagged()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([new HighAmountRule(threshold: 500m)]);
        var session = MakeSession("acc1", amount: 600m);
        var result = evaluator.Evaluate(session);
        Assert.Equal(DecisionOutcome.Flagged, result.Outcome);
    }

    [Fact]
    public void Evaluate_ThreeOrMoreSignals_Blocked()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([
            new HighFrequencyRule(threshold: 1),
            new HighAmountRule(threshold: 1m),
            new HighFrequencyRule(threshold: 1), // duplicate to get 3 signals
        ]);
        var session = MakeSession("acc1", count: 5, amount: 100m);
        var result = evaluator.Evaluate(session);
        Assert.Equal(DecisionOutcome.Blocked, result.Outcome);
    }

    [Fact]
    public void Evaluate_BelowThresholds_Approved()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([new HighFrequencyRule(10), new HighAmountRule(10_000m)]);
        var session = MakeSession("acc1", count: 5, amount: 500m);
        var result = evaluator.Evaluate(session);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }

    [Fact]
    public void RefreshRules_ReplacesExistingRules()
    {
        var evaluator = new RuleEvaluator();
        evaluator.RefreshRules([new HighFrequencyRule(threshold: 1)]);
        evaluator.RefreshRules([]); // clear
        var session = MakeSession("acc1", count: 100);
        var result = evaluator.Evaluate(session);
        Assert.Equal(DecisionOutcome.Approved, result.Outcome);
    }
}

public sealed class FraudSessionStoreTests
{
    [Fact]
    public void GetOrCreate_NewEntityId_CreatesSession()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        var session = store.GetOrCreate("acc1");
        Assert.Equal("acc1", session.EntityId);
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void GetOrCreate_ExistingEntityId_ReturnsSameSession()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        var s1 = store.GetOrCreate("acc1");
        var s2 = store.GetOrCreate("acc1");
        Assert.Same(s1, s2);
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void TryGet_ExistingSession_ReturnsTrue()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        store.GetOrCreate("acc1");
        Assert.True(store.TryGet("acc1", out var session));
        Assert.NotNull(session);
    }

    [Fact]
    public void TryGet_NonExistentSession_ReturnsFalse()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        Assert.False(store.TryGet("acc-unknown", out _));
    }

    [Fact]
    public void EvictIdle_RemovesOldSessions()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        var session = store.GetOrCreate("acc1");
        session.LastActivityAt = DateTimeOffset.UtcNow.AddHours(-2);
        var threshold = DateTimeOffset.UtcNow.AddHours(-1);
        var evicted = store.EvictIdle(threshold);
        Assert.Equal(1, evicted);
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void EvictIdle_KeepsRecentSessions()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        store.GetOrCreate("acc1");
        var threshold = DateTimeOffset.UtcNow.AddHours(-1);
        var evicted = store.EvictIdle(threshold);
        Assert.Equal(0, evicted);
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void Snapshot_ReturnsAllSessions()
    {
        var store = new FraudSessionStore(UtcSystemClock.Instance);
        store.GetOrCreate("acc1");
        store.GetOrCreate("acc2");
        var snapshot = store.Snapshot();
        Assert.Equal(2, snapshot.Count);
    }
}

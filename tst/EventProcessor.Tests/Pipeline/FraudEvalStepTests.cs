using EventProcessor.Models;
using EventProcessor.Services;
using EventProcessor.Workers.Pipeline;
using FluentAssertions;
using KoreForge.Processing.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Tests.Pipeline;

public sealed class FraudEvalStepTests : IDisposable
{
    private readonly FakeSessionStore _sessionStore = new();
    private readonly FakeProducer _producer = new();
    private readonly FraudEvalStep _step;

    public FraudEvalStepTests()
    {
        var ruleEngine = new SimpleFraudRuleEngine(
            new FakeOptionsMonitor(ValidOptions()),
            TestLogHelper.CreateLog<SimpleFraudRuleEngine>());

        _step = new FraudEvalStep(
            ruleEngine,
            _sessionStore,
            _producer,
            NullLogger<FraudEvalStep>.Instance);
    }

    public void Dispose()
    {
        _sessionStore.Dispose();
        _producer.Dispose();
    }

    [Fact]
    public async Task Low_amount_transaction_yields_allow_decision()
    {
        var extracted = JObject.FromObject(new
        {
            nid = "NID-001",
            transactionId = "TX-001",
            amount = 50m,
            countryCode = "US",
        });

        var outcome = await _step.InvokeAsync(extracted, new PipelineContext(), default);

        outcome.Kind.Should().Be(StepOutcomeKind.Continue);
        outcome.Value!.Decision.Should().Be(DecisionType.Allow);
        outcome.Value!.Score.Should().Be(0);
    }

    [Fact]
    public async Task High_amount_triggers_flagged_or_blocked()
    {
        var extracted = JObject.FromObject(new
        {
            nid = "NID-002",
            transactionId = "TX-002",
            amount = 55000m,
            countryCode = "US",
        });

        var outcome = await _step.InvokeAsync(extracted, new PipelineContext(), default);

        outcome.Kind.Should().Be(StepOutcomeKind.Continue);
        // VeryHighAmount (0.6) + HighAmount (0.3) = 0.9 → Flagged
        outcome.Value!.Score.Should().BeGreaterThan(0);
        outcome.Value!.Decision.Should().NotBe(DecisionType.Allow);
    }

    [Fact]
    public async Task Session_state_accumulates_across_transactions()
    {
        var nid = "NID-ACCUM";

        for (int i = 0; i < 3; i++)
        {
            var extracted = JObject.FromObject(new
            {
                nid,
                transactionId = $"TX-{i}",
                amount = 100m,
                countryCode = "US",
            });
            await _step.InvokeAsync(extracted, new PipelineContext(), default);
        }

        var session = await _sessionStore.GetOrCreateAsync(nid);
        session.TransactionCount.Should().Be(3);
        session.TotalAmount.Should().Be(300m);
    }

    [Fact]
    public async Task Session_base_country_set_from_first_transaction()
    {
        var extracted = JObject.FromObject(new
        {
            nid = "NID-COUNTRY",
            transactionId = "TX-1",
            amount = 10m,
            countryCode = "NO",
        });

        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-COUNTRY");
        session.BaseCountry.Should().Be("NO");
    }

    [Fact]
    public async Task Unusual_location_triggers_rule_when_country_differs()
    {
        var nid = "NID-TRAVEL";

        // First transaction sets base country.
        var first = JObject.FromObject(new { nid, transactionId = "TX-1", amount = 10m, countryCode = "US" });
        await _step.InvokeAsync(first, new PipelineContext(), default);

        // Second transaction from different country.
        var second = JObject.FromObject(new { nid, transactionId = "TX-2", amount = 10m, countryCode = "RU" });
        var outcome = await _step.InvokeAsync(second, new PipelineContext(), default);

        outcome.Value!.TriggeredRuleNames.Should().Contain("UnusualLocation");
    }

    [Fact]
    public async Task Producer_receives_decision()
    {
        var extracted = JObject.FromObject(new
        {
            nid = "NID-PRODUCE",
            transactionId = "TX-1",
            amount = 10m,
        });

        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        _producer.Produced.Should().HaveCount(1);
        _producer.Produced[0].Nid.Should().Be("NID-PRODUCE");
    }

    [Fact]
    public async Task Transactions_grow_unbounded_as_lifetime_history()
    {
        var nid = "NID-LIFETIME";
        for (int i = 0; i < 110; i++)
        {
            var extracted = JObject.FromObject(new { nid, transactionId = $"TX-{i}", amount = 1m });
            await _step.InvokeAsync(extracted, new PipelineContext(), default);
        }

        var session = await _sessionStore.GetOrCreateAsync(nid);
        session.Transactions.Count.Should().Be(110);
    }

    [Fact]
    public async Task Score_above_1_yields_blocked_decision()
    {
        // HighAmount (0.3) + VeryHighAmount (0.6) + UnusualLocation (0.5) = 1.4 → Blocked
        var first = JObject.FromObject(new { nid = "NID-BLOCK", transactionId = "TX-0", amount = 10m, countryCode = "US" });
        await _step.InvokeAsync(first, new PipelineContext(), default);

        var second = JObject.FromObject(new { nid = "NID-BLOCK", transactionId = "TX-1", amount = 55000m, countryCode = "RU" });
        var outcome = await _step.InvokeAsync(second, new PipelineContext(), default);

        outcome.Value!.Decision.Should().Be(DecisionType.Blocked);
        outcome.Value!.Score.Should().BeGreaterThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task Missing_nid_uses_empty_string()
    {
        var extracted = JObject.FromObject(new { transactionId = "TX-1", amount = 10m });
        var outcome = await _step.InvokeAsync(extracted, new PipelineContext(), default);

        outcome.Kind.Should().Be(StepOutcomeKind.Continue);
        // Session is keyed on empty string
        var session = await _sessionStore.GetOrCreateAsync(string.Empty);
        session.TransactionCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Missing_amount_defaults_to_zero()
    {
        var extracted = JObject.FromObject(new { nid = "NID-NOAMT", transactionId = "TX-1", countryCode = "US" });
        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-NOAMT");
        session.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Session_earliest_transaction_set_on_first_tx()
    {
        var extracted = JObject.FromObject(new { nid = "NID-EARLIEST", transactionId = "TX-1", amount = 10m });
        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-EARLIEST");
        session.EarliestTransactionAt.Should().NotBeNull();
        session.EarliestTransactionAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Session_decision_field_set_after_evaluation()
    {
        var extracted = JObject.FromObject(new { nid = "NID-DEC", transactionId = "TX-1", amount = 10m, countryCode = "US" });
        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-DEC");
        session.Decision.Should().NotBeNull();
        session.Decision!.Decision.Should().Be(DecisionType.Allow);
    }

    [Fact]
    public async Task Null_country_code_leaves_base_country_null()
    {
        var extracted = new JObject { ["nid"] = "NID-NULLC", ["transactionId"] = "TX-1", ["amount"] = 10 };
        // countryCode not set at all
        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-NULLC");
        session.BaseCountry.Should().BeNull();
    }

    [Fact]
    public async Task Session_triggered_rules_populated()
    {
        var extracted = JObject.FromObject(new { nid = "NID-RULES", transactionId = "TX-1", amount = 55000m, countryCode = "US" });
        await _step.InvokeAsync(extracted, new PipelineContext(), default);

        var session = await _sessionStore.GetOrCreateAsync("NID-RULES");
        session.TriggeredRules.Should().NotBeEmpty();
        session.TriggeredRules.Select(r => r.RuleName).Should().Contain("HighAmount");
        session.TriggeredRules.Select(r => r.RuleName).Should().Contain("VeryHighAmount");
    }

    // ── Test doubles ────────────────────────────────────────────────

    private static FraudEngineOptions ValidOptions() => new()
    {
        Kafka = new KafkaOptions
        {
            Consumer = new ConsumerOptions { BootstrapServers = "localhost:9092", GroupId = "g", Topics = ["t"] }
        },
        Processing = new ProcessingOptions { MaxBatchSize = 100, BatchTimeoutMs = 50, BucketCount = 4 },
        Flush = new FlushOptions { TimeBasedIntervalMs = 1000, CountThreshold = 500, DirtyRatioThreshold = 0.3, MemoryPressureThreshold = 0.85 },
        Sessions = new SessionOptions { IdleTimeoutMinutes = 30, MaxTransactionsPerSession = 10000, MaxSessionDurationMinutes = 1440 },
        Scoring = new ScoringOptions { DecisionThreshold = 0.75, HighScoreAlertThreshold = 0.5 },
        Rules =
        [
            new RuleOptions { Name = "HighAmount", Expression = "tx.amount > 10000", ScoreModifier = 0.3, Enabled = true },
            new RuleOptions { Name = "VeryHighAmount", Expression = "tx.amount > 50000", ScoreModifier = 0.6, Enabled = true },
            new RuleOptions { Name = "RapidTransactions", Expression = "session.txCount > 10", ScoreModifier = 0.4, Enabled = true },
            new RuleOptions { Name = "UnusualLocation", Expression = "tx.country != session.baseCountry", ScoreModifier = 0.5, Enabled = true },
            new RuleOptions { Name = "LargeSessionTotal", Expression = "session.totalAmount > 100000", ScoreModifier = 0.7, Enabled = true },
        ],
    };

    private sealed class FakeOptionsMonitor(FraudEngineOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<FraudEngineOptions>
    {
        public FraudEngineOptions CurrentValue => value;
        public FraudEngineOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FraudEngineOptions, string?> listener) => null;
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        private readonly Dictionary<string, FraudSession> _sessions = new();
        private readonly Dictionary<string, DateTimeOffset> _dirty = new();

        public ValueTask<FraudSession> GetOrCreateAsync(string nid)
        {
            if (_sessions.TryGetValue(nid, out var existing))
                return new ValueTask<FraudSession>(existing);

            var session = new FraudSession
            {
                NID = nid,
                Status = SessionStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActivityAt = DateTimeOffset.UtcNow,
            };
            _sessions[nid] = session;
            return new ValueTask<FraudSession>(session);
        }

        public void Put(string nid, FraudSession session)
        {
            _sessions[nid] = session;
            _dirty[nid] = DateTimeOffset.UtcNow;
        }

        public IReadOnlyList<(string Nid, FraudSession Session)> DrainDirty(int maxCount) =>
            _dirty.OrderBy(kv => kv.Value).Take(maxCount).Select(kv => (kv.Key, _sessions[kv.Key])).ToList();

        public int EvictIdle(TimeSpan idleTimeout) => 0;
        public Task CheckpointAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RecoverAsync(CancellationToken ct = default) => Task.CompletedTask;

        public long Count => _sessions.Count;
        public long DirtyCount => _dirty.Count;
        public int BucketCount => 4;
        public int GetBucket(string nid) => (nid.GetHashCode() & 0x7FFFFFFF) % BucketCount;
        public void Dispose() { }
    }

    private sealed class FakeProducer : IFraudDecisionProducer
    {
        public List<(string Nid, FraudDecision Decision)> Produced { get; } = new();

        public Task ProduceAsync(string nid, FraudDecision decision, CancellationToken ct)
        {
            Produced.Add((nid, decision));
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}

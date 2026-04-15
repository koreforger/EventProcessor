using EventProcessor.Models;
using EventProcessor.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EventProcessor.Tests;

public sealed class SimpleFraudRuleEngineTests
{
    private static FraudEngineOptions MakeOptions(params RuleOptions[] rules) => new()
    {
        Kafka = new KafkaOptions
        {
            Consumer = new ConsumerOptions { BootstrapServers = "localhost:9092", GroupId = "g", Topics = ["t"] }
        },
        Processing = new ProcessingOptions { MaxBatchSize = 100, BatchTimeoutMs = 50 },
        Flush = new FlushOptions { TimeBasedIntervalMs = 1000, CountThreshold = 500, DirtyRatioThreshold = 0.3, MemoryPressureThreshold = 0.85 },
        Sessions = new SessionOptions { IdleTimeoutMinutes = 30, MaxTransactionsPerSession = 10000, MaxSessionDurationMinutes = 1440 },
        Scoring = new ScoringOptions { DecisionThreshold = 0.75, HighScoreAlertThreshold = 0.5 },
        Rules = rules.ToList(),
    };

    private static SimpleFraudRuleEngine CreateEngine(params RuleOptions[] rules)
    {
        var monitor = new FakeOptionsMonitor(MakeOptions(rules));
        return new SimpleFraudRuleEngine(monitor, TestLogHelper.CreateLog<SimpleFraudRuleEngine>());
    }

    [Fact]
    public void HighAmount_matches_above_10000()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true });
        var tx = new TransactionEvent { Amount = 15000m };
        var session = new FraudSession();

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "HighAmount" && r.IsMatch);
        results[0].ScoreModifier.Should().Be(0.3);
    }

    [Fact]
    public void HighAmount_does_not_match_below_10000()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true });
        var tx = new TransactionEvent { Amount = 5000m };
        var session = new FraudSession();

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "HighAmount");
        results[0].IsMatch.Should().BeFalse();
    }

    [Fact]
    public void VeryHighAmount_matches_above_50000()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "VeryHighAmount", Expression = "", ScoreModifier = 0.6, Enabled = true });
        var tx = new TransactionEvent { Amount = 75000m };
        var session = new FraudSession();

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "VeryHighAmount" && r.IsMatch);
    }

    [Fact]
    public void RapidTransactions_matches_above_10_count()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "RapidTransactions", Expression = "", ScoreModifier = 0.4, Enabled = true });
        var tx = new TransactionEvent();
        var session = new FraudSession { TransactionCount = 15 };

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "RapidTransactions" && r.IsMatch);
    }

    [Fact]
    public void UnusualLocation_matches_different_country()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "UnusualLocation", Expression = "", ScoreModifier = 0.5, Enabled = true });
        var tx = new TransactionEvent { CountryCode = "RU" };
        var session = new FraudSession { BaseCountry = "US" };

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "UnusualLocation" && r.IsMatch);
    }

    [Fact]
    public void UnusualLocation_does_not_match_same_country()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "UnusualLocation", Expression = "", ScoreModifier = 0.5, Enabled = true });
        var tx = new TransactionEvent { CountryCode = "US" };
        var session = new FraudSession { BaseCountry = "US" };

        var results = engine.Evaluate(tx, session);

        results[0].IsMatch.Should().BeFalse();
    }

    [Fact]
    public void LargeSessionTotal_matches_above_100000()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "LargeSessionTotal", Expression = "", ScoreModifier = 0.7, Enabled = true });
        var tx = new TransactionEvent();
        var session = new FraudSession { TotalAmount = 150000m };

        var results = engine.Evaluate(tx, session);

        results.Should().ContainSingle(r => r.RuleName == "LargeSessionTotal" && r.IsMatch);
    }

    [Fact]
    public void Disabled_rule_is_skipped()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = false });
        var tx = new TransactionEvent { Amount = 99999m };
        var session = new FraudSession();

        var results = engine.Evaluate(tx, session);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_rule_name_produces_compile_error()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "CustomRule", Expression = "some.jex", ScoreModifier = 0.1, Enabled = true });

        var rules = engine.GetRules();

        rules.Should().ContainSingle(r => r.Name == "CustomRule");
        rules[0].CompileError.Should().Contain("no built-in implementation");
        rules[0].CompiledPredicate.Should().BeNull();
    }

    [Fact]
    public void Multiple_rules_evaluated_together()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true },
            new RuleOptions { Name = "VeryHighAmount", Expression = "", ScoreModifier = 0.6, Enabled = true });
        var tx = new TransactionEvent { Amount = 75000m };
        var session = new FraudSession();

        var results = engine.Evaluate(tx, session);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.IsMatch);
        results.Sum(r => r.ScoreModifier).Should().BeApproximately(0.9, 1e-10);
    }

    [Fact]
    public void GetRules_returns_all_loaded_rules()
    {
        var engine = CreateEngine(
            new RuleOptions { Name = "HighAmount", Expression = "expr1", ScoreModifier = 0.3, Enabled = true },
            new RuleOptions { Name = "VeryHighAmount", Expression = "expr2", ScoreModifier = 0.6, Enabled = true });

        var rules = engine.GetRules();

        rules.Should().HaveCount(2);
    }

    [Fact]
    public void ReloadRules_replaces_existing_rules()
    {
        var monitor = new FakeOptionsMonitor(MakeOptions(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true }));
        var engine = new SimpleFraudRuleEngine(monitor, TestLogHelper.CreateLog<SimpleFraudRuleEngine>());

        engine.GetRules().Should().HaveCount(1);

        monitor.Update(MakeOptions(
            new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true },
            new RuleOptions { Name = "VeryHighAmount", Expression = "", ScoreModifier = 0.6, Enabled = true }));
        engine.ReloadRules();

        engine.GetRules().Should().HaveCount(2);
    }

    // ── Test doubles ────────────────────────────────────────────────

    private sealed class FakeOptionsMonitor(FraudEngineOptions value) : IOptionsMonitor<FraudEngineOptions>
    {
        private FraudEngineOptions _value = value;
        public FraudEngineOptions CurrentValue => _value;
        public FraudEngineOptions Get(string? name) => _value;
        public IDisposable? OnChange(Action<FraudEngineOptions, string?> listener) => null;
        public void Update(FraudEngineOptions newValue) => _value = newValue;
    }
}

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using EventProcessor.Models;
using EventProcessor.Services;
using Microsoft.Extensions.Options;

namespace EventProcessor.Tests.Benchmarks;

/// <summary>
/// Performance benchmarks for the FASTER-backed session store.
/// Run with: dotnet run -c Release --project tst/EventProcessor.Tests -- --filter "SessionStoreBenchmarks*"
/// Or from test: set BENCHMARK_RUN=1 and run the trigger test.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SessionStoreBenchmarks
{
    private FasterSessionStore _store = null!;
    private string[] _nids = null!;

    [Params(1000, 10_000)]
    public int SessionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new FraudEngineOptions
        {
            Processing = new ProcessingOptions { BucketCount = 64 },
        });
        _store = new FasterSessionStore(options, TestLogHelper.CreateLog<FasterSessionStore>());

        _nids = new string[SessionCount];
        for (int i = 0; i < SessionCount; i++)
            _nids[i] = $"NID-{i:D8}";
    }

    [GlobalCleanup]
    public void Cleanup() => _store.Dispose();

    [Benchmark(Description = "GetOrCreate (cold)")]
    public void GetOrCreate_Cold()
    {
        for (int i = 0; i < SessionCount; i++)
            _store.GetOrCreate(_nids[i]);
    }

    [Benchmark(Description = "GetOrCreate (warm)")]
    public void GetOrCreate_Warm()
    {
        // Sessions already exist from previous benchmark iteration
        for (int i = 0; i < SessionCount; i++)
            _store.GetOrCreate(_nids[i]);
    }

    [Benchmark(Description = "Put + GetOrCreate")]
    public void Put_Then_Get()
    {
        for (int i = 0; i < SessionCount; i++)
        {
            var session = new FraudSession
            {
                NID = _nids[i],
                TransactionCount = i,
                TotalAmount = i * 100m,
            };
            _store.Put(_nids[i], session);
        }
        for (int i = 0; i < SessionCount; i++)
            _store.GetOrCreate(_nids[i]);
    }

    [Benchmark(Description = "DrainDirty (100)")]
    public void DrainDirty_100()
    {
        // Ensure dirty set has entries
        for (int i = 0; i < Math.Min(200, SessionCount); i++)
            _store.Put(_nids[i], new FraudSession { NID = _nids[i] });

        _store.DrainDirty(100);
    }
}

/// <summary>
/// Performance benchmarks for the fraud rule engine.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RuleEngineBenchmarks
{
    private SimpleFraudRuleEngine _engine = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new FraudEngineOptions
        {
            Kafka = new KafkaOptions
            {
                Consumer = new ConsumerOptions { BootstrapServers = "localhost:9092", GroupId = "g", Topics = ["t"] }
            },
            Processing = new ProcessingOptions { MaxBatchSize = 100, BatchTimeoutMs = 50 },
            Flush = new FlushOptions { TimeBasedIntervalMs = 1000, CountThreshold = 500, DirtyRatioThreshold = 0.3, MemoryPressureThreshold = 0.85 },
            Sessions = new SessionOptions { IdleTimeoutMinutes = 30, MaxTransactionsPerSession = 10000, MaxSessionDurationMinutes = 1440 },
            Scoring = new ScoringOptions { DecisionThreshold = 0.75, HighScoreAlertThreshold = 0.5 },
            Rules =
            [
                new RuleOptions { Name = "HighAmount", Expression = "", ScoreModifier = 0.3, Enabled = true },
                new RuleOptions { Name = "VeryHighAmount", Expression = "", ScoreModifier = 0.6, Enabled = true },
                new RuleOptions { Name = "RapidTransactions", Expression = "", ScoreModifier = 0.4, Enabled = true },
                new RuleOptions { Name = "UnusualLocation", Expression = "", ScoreModifier = 0.5, Enabled = true },
                new RuleOptions { Name = "LargeSessionTotal", Expression = "", ScoreModifier = 0.7, Enabled = true },
            ],
        };

        _engine = new SimpleFraudRuleEngine(
            new FakeOptionsMonitor(options),
            TestLogHelper.CreateLog<SimpleFraudRuleEngine>());
    }

    [Benchmark(Description = "Evaluate 5 rules (no match)")]
    public void Evaluate_NoMatch()
    {
        var tx = new TransactionEvent { Amount = 100m, CountryCode = "US" };
        var session = new FraudSession { BaseCountry = "US", TransactionCount = 1, TotalAmount = 100m };
        _engine.Evaluate(tx, session);
    }

    [Benchmark(Description = "Evaluate 5 rules (all match)")]
    public void Evaluate_AllMatch()
    {
        var tx = new TransactionEvent { Amount = 75000m, CountryCode = "RU" };
        var session = new FraudSession
        {
            BaseCountry = "US",
            TransactionCount = 20,
            TotalAmount = 200000m,
        };
        _engine.Evaluate(tx, session);
    }

    [Benchmark(Description = "Evaluate 1000 transactions")]
    public void Evaluate_Batch_1000()
    {
        var session = new FraudSession { BaseCountry = "US" };
        for (int i = 0; i < 1000; i++)
        {
            var tx = new TransactionEvent { Amount = i * 10m, CountryCode = "US" };
            session.TransactionCount++;
            session.TotalAmount += tx.Amount;
            _engine.Evaluate(tx, session);
        }
    }

    private sealed class FakeOptionsMonitor(FraudEngineOptions value) : IOptionsMonitor<FraudEngineOptions>
    {
        public FraudEngineOptions CurrentValue => value;
        public FraudEngineOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<FraudEngineOptions, string?> listener) => null;
    }
}

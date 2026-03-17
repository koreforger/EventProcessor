using EventProcessor;
using EventProcessor.Configuration;
using Microsoft.Extensions.Options;

namespace EventProcessor.Tests;

public sealed class FraudEngineOptionsValidatorTests
{
    private static FraudEngineOptions ValidOptions() => new()
    {
        Kafka = new KafkaOptions
        {
            Consumer = new ConsumerOptions
            {
                BootstrapServers = "localhost:9092",
                GroupId = "test-group",
                Topics = ["test.topic"],
            }
        },
        Processing = new ProcessingOptions { MaxBatchSize = 100, BatchTimeoutMs = 50 },
        Flush = new FlushOptions
        {
            TimeBasedIntervalMs = 1000,
            CountThreshold = 500,
            DirtyRatioThreshold = 0.3,
            MemoryPressureThreshold = 0.85,
        },
        Sessions = new SessionOptions
        {
            IdleTimeoutMinutes = 30,
            MaxTransactionsPerSession = 10_000,
            MaxSessionDurationMinutes = 1440,
        },
        Scoring = new ScoringOptions
        {
            DecisionThreshold = 0.75,
            HighScoreAlertThreshold = 0.5,
        },
    };

    private static readonly FraudEngineOptionsValidator Validator = new();

    [Fact]
    public void Valid_options_pass()
    {
        var result = Validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_bootstrap_servers_fails()
    {
        var options = ValidOptions();
        options.Kafka.Consumer.BootstrapServers = null;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("BootstrapServers", result.FailureMessage);
    }

    [Fact]
    public void Missing_group_id_fails()
    {
        var options = ValidOptions();
        options.Kafka.Consumer.GroupId = null;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("GroupId", result.FailureMessage);
    }

    [Fact]
    public void Empty_topics_fails()
    {
        var options = ValidOptions();
        options.Kafka.Consumer.Topics = [];
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("Topics", result.FailureMessage);
    }

    [Fact]
    public void Zero_max_batch_size_fails()
    {
        var options = ValidOptions();
        options.Processing.MaxBatchSize = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("MaxBatchSize", result.FailureMessage);
    }

    [Fact]
    public void Zero_flush_interval_fails()
    {
        var options = ValidOptions();
        options.Flush.TimeBasedIntervalMs = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("TimeBasedIntervalMs", result.FailureMessage);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    public void Dirty_ratio_out_of_range_fails(double value)
    {
        var options = ValidOptions();
        options.Flush.DirtyRatioThreshold = value;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("DirtyRatioThreshold", result.FailureMessage);
    }

    [Fact]
    public void High_score_threshold_above_decision_threshold_fails()
    {
        var options = ValidOptions();
        options.Scoring.DecisionThreshold = 0.5;
        options.Scoring.HighScoreAlertThreshold = 0.6; // higher than decision threshold
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("HighScoreAlertThreshold", result.FailureMessage);
    }

    [Fact]
    public void Multiple_errors_accumulated_in_one_result()
    {
        var options = new FraudEngineOptions(); // all defaults — zero for required numbers
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        // At minimum: BootstrapServers, GroupId, Topics, MaxBatchSize, BatchTimeoutMs,
        // TimeBasedIntervalMs, CountThreshold, DirtyRatioThreshold, MemoryPressureThreshold,
        // IdleTimeoutMinutes, MaxTransactionsPerSession, MaxSessionDurationMinutes,
        // DecisionThreshold, HighScoreAlertThreshold
        Assert.True((result.Failures?.Count() ?? 0) > 5, $"Expected >5 failures, got: {result.FailureMessage}");
    }
}

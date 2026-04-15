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
    public void High_score_threshold_equal_to_decision_threshold_fails()
    {
        var options = ValidOptions();
        options.Scoring.DecisionThreshold = 0.75;
        options.Scoring.HighScoreAlertThreshold = 0.75;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("HighScoreAlertThreshold", result.FailureMessage);
    }

    [Fact]
    public void BatchTimeoutMs_zero_fails()
    {
        var options = ValidOptions();
        options.Processing.BatchTimeoutMs = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("BatchTimeoutMs", result.FailureMessage);
    }

    [Fact]
    public void CountThreshold_zero_fails()
    {
        var options = ValidOptions();
        options.Flush.CountThreshold = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("CountThreshold", result.FailureMessage);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.1)]
    public void MemoryPressureThreshold_out_of_range_fails(double value)
    {
        var options = ValidOptions();
        options.Flush.MemoryPressureThreshold = value;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("MemoryPressureThreshold", result.FailureMessage);
    }

    [Fact]
    public void IdleTimeoutMinutes_zero_fails()
    {
        var options = ValidOptions();
        options.Sessions.IdleTimeoutMinutes = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("IdleTimeoutMinutes", result.FailureMessage);
    }

    [Fact]
    public void MaxTransactionsPerSession_zero_fails()
    {
        var options = ValidOptions();
        options.Sessions.MaxTransactionsPerSession = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("MaxTransactionsPerSession", result.FailureMessage);
    }

    [Fact]
    public void MaxSessionDurationMinutes_zero_fails()
    {
        var options = ValidOptions();
        options.Sessions.MaxSessionDurationMinutes = 0;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("MaxSessionDurationMinutes", result.FailureMessage);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void DecisionThreshold_out_of_range_fails(double value)
    {
        var options = ValidOptions();
        options.Scoring.DecisionThreshold = value;
        // Also fix alert threshold to avoid cascading failure
        options.Scoring.HighScoreAlertThreshold = value > 0 ? value - 0.01 : 0.01;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("DecisionThreshold", result.FailureMessage);
    }

    [Fact]
    public void Null_topics_fails()
    {
        var options = ValidOptions();
        options.Kafka.Consumer.Topics = null;
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("Topics", result.FailureMessage);
    }

    [Fact]
    public void SqlSink_with_connection_string_and_zero_batch_size_fails()
    {
        var options = ValidOptions();
        options.SqlSink = new SqlSinkOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            BatchSize = 0,
            TimeoutSeconds = 30,
        };
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("BatchSize", result.FailureMessage);
    }

    [Fact]
    public void SqlSink_with_connection_string_and_zero_timeout_fails()
    {
        var options = ValidOptions();
        options.SqlSink = new SqlSinkOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            BatchSize = 500,
            TimeoutSeconds = 0,
        };
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains("TimeoutSeconds", result.FailureMessage);
    }

    [Fact]
    public void SqlSink_without_connection_string_ignores_batch_params()
    {
        var options = ValidOptions();
        options.SqlSink = new SqlSinkOptions
        {
            ConnectionString = null,
            BatchSize = 0,
            TimeoutSeconds = 0,
        };
        var result = Validator.Validate(null, options);
        Assert.True(result.Succeeded);
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

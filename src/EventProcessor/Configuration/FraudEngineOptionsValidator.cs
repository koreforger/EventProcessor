using Microsoft.Extensions.Options;

namespace EventProcessor.Configuration;

/// <summary>
/// Validates FraudEngineOptions at application startup.
/// All operational values (thresholds, batch sizes, connectivity strings) must be
/// explicitly configured — there are no silent defaults.
/// </summary>
public sealed class FraudEngineOptionsValidator : IValidateOptions<FraudEngineOptions>
{
    public ValidateOptionsResult Validate(string? name, FraudEngineOptions options)
    {
        var errors = new List<string>();

        // Kafka consumer
        if (string.IsNullOrWhiteSpace(options.Kafka.Consumer.BootstrapServers))
            errors.Add("FraudEngine:Kafka:Consumer:BootstrapServers is required.");

        if (string.IsNullOrWhiteSpace(options.Kafka.Consumer.GroupId))
            errors.Add("FraudEngine:Kafka:Consumer:GroupId is required.");

        if (options.Kafka.Consumer.Topics is null || options.Kafka.Consumer.Topics.Count == 0)
            errors.Add("FraudEngine:Kafka:Consumer:Topics must contain at least one topic.");

        // Kafka producer — only required when BootstrapServers is provided
        if (!string.IsNullOrWhiteSpace(options.Kafka.Producer.BootstrapServers))
        {
            // Producer is configured — nothing else required on the producer side right now
        }

        // Processing
        if (options.Processing.MaxBatchSize <= 0)
            errors.Add("FraudEngine:Processing:MaxBatchSize must be greater than zero.");

        if (options.Processing.BatchTimeoutMs <= 0)
            errors.Add("FraudEngine:Processing:BatchTimeoutMs must be greater than zero.");

        // Flush
        if (options.Flush.TimeBasedIntervalMs <= 0)
            errors.Add("FraudEngine:Flush:TimeBasedIntervalMs must be greater than zero.");

        if (options.Flush.CountThreshold <= 0)
            errors.Add("FraudEngine:Flush:CountThreshold must be greater than zero.");

        if (options.Flush.DirtyRatioThreshold <= 0.0 || options.Flush.DirtyRatioThreshold >= 1.0)
            errors.Add("FraudEngine:Flush:DirtyRatioThreshold must be in range (0.0, 1.0).");

        if (options.Flush.MemoryPressureThreshold <= 0.0 || options.Flush.MemoryPressureThreshold >= 1.0)
            errors.Add("FraudEngine:Flush:MemoryPressureThreshold must be in range (0.0, 1.0).");

        // Sessions
        if (options.Sessions.IdleTimeoutMinutes <= 0)
            errors.Add("FraudEngine:Sessions:IdleTimeoutMinutes must be greater than zero.");

        if (options.Sessions.MaxTransactionsPerSession <= 0)
            errors.Add("FraudEngine:Sessions:MaxTransactionsPerSession must be greater than zero.");

        if (options.Sessions.MaxSessionDurationMinutes <= 0)
            errors.Add("FraudEngine:Sessions:MaxSessionDurationMinutes must be greater than zero.");

        // Scoring
        if (options.Scoring.DecisionThreshold <= 0.0 || options.Scoring.DecisionThreshold > 1.0)
            errors.Add("FraudEngine:Scoring:DecisionThreshold must be in range (0.0, 1.0].");

        if (options.Scoring.HighScoreAlertThreshold <= 0.0 || options.Scoring.HighScoreAlertThreshold > 1.0)
            errors.Add("FraudEngine:Scoring:HighScoreAlertThreshold must be in range (0.0, 1.0].");

        if (options.Scoring.HighScoreAlertThreshold >= options.Scoring.DecisionThreshold)
            errors.Add("FraudEngine:Scoring:HighScoreAlertThreshold must be less than DecisionThreshold.");

        // SQL sink — if connection string is set, batch parameters are required
        if (!string.IsNullOrWhiteSpace(options.SqlSink.ConnectionString))
        {
            if (options.SqlSink.BatchSize <= 0)
                errors.Add("FraudEngine:SqlSink:BatchSize must be greater than zero when ConnectionString is set.");

            if (options.SqlSink.TimeoutSeconds <= 0)
                errors.Add("FraudEngine:SqlSink:TimeoutSeconds must be greater than zero when ConnectionString is set.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

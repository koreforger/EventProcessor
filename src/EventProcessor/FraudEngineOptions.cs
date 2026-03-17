namespace EventProcessor;

/// <summary>
/// Configuration options for the FraudEngine.
/// </summary>
public class FraudEngineOptions
{
    /// <summary>
    /// Processing configuration.
    /// </summary>
    public ProcessingOptions Processing { get; set; } = new();

    /// <summary>
    /// Flush configuration.
    /// </summary>
    public FlushOptions Flush { get; set; } = new();

    /// <summary>
    /// Session configuration.
    /// </summary>
    public SessionOptions Sessions { get; set; } = new();

    /// <summary>
    /// Scoring configuration.
    /// </summary>
    public ScoringOptions Scoring { get; set; } = new();

    /// <summary>
    /// Kafka configuration.
    /// </summary>
    public KafkaOptions Kafka { get; set; } = new();

    /// <summary>
    /// SQL sink configuration.
    /// </summary>
    public SqlSinkOptions SqlSink { get; set; } = new();

    /// <summary>
    /// Rule configurations.
    /// </summary>
    public List<RuleOptions> Rules { get; set; } = new();
}

public class ProcessingOptions
{
    public int ConsumerThreads { get; set; } = 8;
    public int BucketCount { get; set; } = 64;
    public int BucketQueueCapacity { get; set; } = 50000;
    public int MaxBatchSize { get; set; } = 500;
    public int BatchTimeoutMs { get; set; } = 100;
}

public class FlushOptions
{
    public int TimeBasedIntervalMs { get; set; } = 5000;
    public int CountThreshold { get; set; } = 1000;
    public double DirtyRatioThreshold { get; set; } = 0.3;
    public double MemoryPressureThreshold { get; set; } = 0.85;
}

public class SessionOptions
{
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int MaxTransactionsPerSession { get; set; } = 10000;
    public int MaxSessionDurationMinutes { get; set; } = 1440;
}

public class ScoringOptions
{
    public double BaseScore { get; set; } = 0.0;
    public double DecisionThreshold { get; set; } = 0.75;
    public double HighScoreAlertThreshold { get; set; } = 0.5;
}

public class KafkaOptions
{
    public ConsumerOptions Consumer { get; set; } = new();
    public ProducerOptions Producer { get; set; } = new();
}

public class ConsumerOptions
{
    public string BootstrapServers { get; set; } = "localhost:29092";
    public string GroupId { get; set; } = "fraud-engine";
    public List<string> Topics { get; set; } = new() { "fraud.transactions" };
    public string AutoOffsetReset { get; set; } = "earliest";
}

public class ProducerOptions
{
    public string BootstrapServers { get; set; } = "localhost:29092";
    public string Acks { get; set; } = "all";
}

public class SqlSinkOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 500;
    public int TimeoutSeconds { get; set; } = 30;
}

public class RuleOptions
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public double ScoreModifier { get; set; }
    public bool Enabled { get; set; } = true;
}

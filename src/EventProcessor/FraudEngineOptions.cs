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
    // Technical capacity defaults — acceptable; they govern internal threading, not operational thresholds.
    public int ConsumerThreads { get; set; } = 8;
    public int BucketCount { get; set; } = 64;
    public int BucketQueueCapacity { get; set; } = 50000;

    /// <summary>Required — set in appsettings (not here). Validated at startup.</summary>
    public int MaxBatchSize { get; set; }

    /// <summary>Required — set in appsettings (not here). Validated at startup.</summary>
    public int BatchTimeoutMs { get; set; }

    /// <summary>Directory for FASTER checkpoint files. Defaults to temp directory.</summary>
    public string? CheckpointDirectory { get; set; }
}

public class FlushOptions
{
    /// <summary>Required — validated at startup.</summary>
    public int TimeBasedIntervalMs { get; set; }

    /// <summary>Required — validated at startup.</summary>
    public int CountThreshold { get; set; }

    /// <summary>Required — validated at startup (0.0–1.0).</summary>
    public double DirtyRatioThreshold { get; set; }

    /// <summary>Required — validated at startup (0.0–1.0).</summary>
    public double MemoryPressureThreshold { get; set; }
}

public class SessionOptions
{
    /// <summary>Sessions idle longer than this are evicted from FASTER (default: 40 min).</summary>
    public int IdleTimeoutMinutes { get; set; } = 40;

    /// <summary>Required — validated at startup.</summary>
    public int MaxTransactionsPerSession { get; set; }

    /// <summary>Required — validated at startup.</summary>
    public int MaxSessionDurationMinutes { get; set; }

    /// <summary>When the current slab spans more than this many days, the oldest month is archived (default: 120).</summary>
    public int ArchiveAfterDays { get; set; } = 120;

    /// <summary>On cache miss, load this many days of history from SQL (default: 180).</summary>
    public int LookbackDays { get; set; } = 180;

    /// <summary>Take a FASTER checkpoint every N flush cycles (default: 6, i.e. every ~30s at 5s interval).</summary>
    public int CheckpointEveryFlushCycles { get; set; } = 6;
}

public class ScoringOptions
{
    // Zero is the correct mathematical identity for a base score accumulator.
    public double BaseScore { get; set; } = 0.0;

    /// <summary>Required — validated at startup (0.0–1.0).</summary>
    public double DecisionThreshold { get; set; }

    /// <summary>Required — validated at startup (0.0 &lt; HighScore &lt; DecisionThreshold).</summary>
    public double HighScoreAlertThreshold { get; set; }
}

public class KafkaOptions
{
    public ConsumerOptions Consumer { get; set; } = new();
    public ProducerOptions Producer { get; set; } = new();
}

public class ConsumerOptions
{
    /// <summary>Required — never default to localhost. Validated at startup.</summary>
    public string? BootstrapServers { get; set; }

    /// <summary>Required — validated at startup.</summary>
    public string? GroupId { get; set; }

    /// <summary>Required — must contain at least one topic name. Validated at startup.</summary>
    public List<string>? Topics { get; set; }

    // "earliest" is the correct safe technical default for all new consumer groups.
    public string AutoOffsetReset { get; set; } = "earliest";
}

public class ProducerOptions
{
    /// <summary>Whether the Kafka decision producer is enabled. When false, a no-op producer is used.</summary>
    public bool Enabled { get; set; }

    /// <summary>Required when Enabled — never default to localhost.</summary>
    public string? BootstrapServers { get; set; }

    /// <summary>Target topic for fraud decision messages.</summary>
    public string Topic { get; set; } = "fraud-decisions";

    // "all" is the safe default for producer acknowledgements.
    public string Acks { get; set; } = "all";
}

public class SqlSinkOptions
{
    /// <summary>Optional — if set, BatchSize and TimeoutSeconds are also required.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Required when ConnectionString is set — validated at startup.</summary>
    public int BatchSize { get; set; }

    /// <summary>Required when ConnectionString is set — validated at startup.</summary>
    public int TimeoutSeconds { get; set; }
}

public class RuleOptions
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public double ScoreModifier { get; set; }
    public bool Enabled { get; set; } = true;
}

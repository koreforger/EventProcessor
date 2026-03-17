using MessagePack;

namespace EventProcessor.Models;

/// <summary>
/// Per-NID fraud session state stored in FASTER.
/// </summary>
[MessagePackObject]
public sealed class FraudSession
{
    [Key(0)]
    public string NID { get; set; } = string.Empty;

    [Key(1)]
    public SessionStatus Status { get; set; }

    [Key(2)]
    public DateTimeOffset CreatedAt { get; set; }

    [Key(3)]
    public DateTimeOffset LastActivityAt { get; set; }

    [Key(4)]
    public int TransactionCount { get; set; }

    [Key(5)]
    public decimal TotalAmount { get; set; }

    [Key(6)]
    public List<TransactionRecord> RecentTransactions { get; set; } = new();

    [Key(7)]
    public Dictionary<string, double> FeatureVector { get; set; } = new();

    [Key(8)]
    public double CurrentScore { get; set; }

    [Key(9)]
    public List<RuleResult> TriggeredRules { get; set; } = new();

    [Key(10)]
    public FraudDecision? Decision { get; set; }

    [Key(11)]
    public string? BaseCountry { get; set; }
}

public enum SessionStatus
{
    Active = 0,
    Closed = 1,
    Flagged = 2,
    Blocked = 3
}

/// <summary>
/// Record of a transaction within a session.
/// </summary>
[MessagePackObject]
public sealed class TransactionRecord
{
    [Key(0)]
    public string TransactionId { get; set; } = string.Empty;

    [Key(1)]
    public decimal Amount { get; set; }

    [Key(2)]
    public DateTimeOffset Timestamp { get; set; }

    [Key(3)]
    public string? TransactionType { get; set; }

    [Key(4)]
    public string? CountryCode { get; set; }
}

/// <summary>
/// Result of a rule evaluation.
/// </summary>
[MessagePackObject]
public sealed class RuleResult
{
    [Key(0)]
    public string RuleName { get; set; } = string.Empty;

    [Key(1)]
    public double ScoreAdjustment { get; set; }

    [Key(2)]
    public DateTimeOffset MatchedAt { get; set; }
}

/// <summary>
/// Fraud decision for a session.
/// </summary>
[MessagePackObject]
public sealed class FraudDecision
{
    [Key(0)]
    public double Score { get; set; }

    [Key(1)]
    public DecisionType Decision { get; set; }

    [Key(2)]
    public DateTimeOffset DecidedAt { get; set; }

    [Key(3)]
    public string? Reason { get; set; }

    [Key(4)]
    public List<string> TriggeredRuleNames { get; set; } = new();
}

public enum DecisionType
{
    Allow = 0,
    Flagged = 1,
    Blocked = 2
}

using EventProcessor.Session;

namespace EventProcessor.Rules;

/// <summary>
/// Outcome of a rule evaluation pass.
/// </summary>
public enum DecisionOutcome
{
    Approved,
    Flagged,
    Blocked,
    Deferred,
}

/// <summary>
/// A single rule evaluated against a FraudSession.
/// </summary>
public interface IFraudRule
{
    string RuleName { get; }
    bool Evaluate(FraudSession session, out string? signal);
}

/// <summary>
/// Evaluates all registered rules in order and returns the most severe outcome.
/// Thread-safe: rules list is replaced atomically on refresh.
/// </summary>
public sealed class RuleEvaluator
{
    private volatile IReadOnlyList<IFraudRule> _rules = [];

    public void RefreshRules(IReadOnlyList<IFraudRule> rules)
        => _rules = rules;

    public DecisionResult Evaluate(FraudSession session)
    {
        var rules = _rules;
        var signals = new List<string>();
        var outcome = DecisionOutcome.Approved;

        foreach (var rule in rules)
        {
            if (rule.Evaluate(session, out var signal))
            {
                if (signal is not null)
                    signals.Add(signal);
            }
        }

        // Escalate outcome based on signals count / keywords
        if (signals.Count >= 3)
            outcome = DecisionOutcome.Blocked;
        else if (signals.Count >= 1)
            outcome = DecisionOutcome.Flagged;

        return new DecisionResult(session.EntityId, outcome, signals);
    }
}

/// <summary>Result of evaluating rules against a session.</summary>
public sealed record DecisionResult(
    string EntityId,
    DecisionOutcome Outcome,
    IReadOnlyList<string> Signals);

// --- Built-in rule implementations ---

/// <summary>Flags when event count exceeds a threshold.</summary>
public sealed class HighFrequencyRule : IFraudRule
{
    private readonly int _threshold;

    public HighFrequencyRule(int threshold = 10) => _threshold = threshold;

    public string RuleName => "HighFrequency";

    public bool Evaluate(FraudSession session, out string? signal)
    {
        if (session.EventCount > _threshold)
        {
            signal = $"HighFrequency:{session.EventCount}";
            return true;
        }
        signal = null;
        return false;
    }
}

/// <summary>Flags when total transaction amount exceeds a threshold.</summary>
public sealed class HighAmountRule : IFraudRule
{
    private readonly decimal _threshold;

    public HighAmountRule(decimal threshold = 10_000m) => _threshold = threshold;

    public string RuleName => "HighAmount";

    public bool Evaluate(FraudSession session, out string? signal)
    {
        if (session.TotalAmount > _threshold)
        {
            signal = $"HighAmount:{session.TotalAmount}";
            return true;
        }
        signal = null;
        return false;
    }
}

using EventProcessor.Logging;
using EventProcessor.Models;
using Microsoft.Extensions.Options;

namespace EventProcessor.Services;

/// <summary>
/// Fraud rule engine.
/// Currently uses compiled delegates; will migrate to JEX expressions when available.
/// </summary>
public interface IFraudRuleEngine
{
    /// <summary>
    /// Evaluates all enabled rules against the transaction and session state.
    /// </summary>
    IReadOnlyList<RuleEvaluationResult> Evaluate(TransactionEvent transaction, FraudSession session);

    /// <summary>
    /// Gets all loaded rules.
    /// </summary>
    IReadOnlyList<LoadedRule> GetRules();

    /// <summary>
    /// Reloads rules from configuration.
    /// </summary>
    void ReloadRules();
}

/// <summary>
/// Result of evaluating a single rule.
/// </summary>
public sealed class RuleEvaluationResult
{
    public required string RuleName { get; init; }
    public required bool IsMatch { get; init; }
    public required double ScoreModifier { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// A loaded rule.
/// </summary>
public sealed class LoadedRule
{
    public required string Name { get; init; }
    public required string Expression { get; init; }
    public required double ScoreModifier { get; init; }
    public required bool Enabled { get; init; }
    public required Func<TransactionEvent, FraudSession, bool>? CompiledPredicate { get; init; }
    public string? CompileError { get; init; }
}

/// <summary>
/// Simple fraud rule engine using compiled delegates.
/// </summary>
public sealed class SimpleFraudRuleEngine : IFraudRuleEngine
{
    private readonly IOptionsMonitor<FraudEngineOptions> _options;
    private readonly EventProcessorLog<SimpleFraudRuleEngine> _log;
    private List<LoadedRule> _rules = new();
    private readonly object _rulesLock = new();

    // Built-in rule implementations (hardcoded for now, will be replaced by JEX)
    private static readonly Dictionary<string, Func<TransactionEvent, FraudSession, bool>> BuiltInRules = new()
    {
        ["HighAmount"] = (tx, _) => tx.Amount > 10000,
        ["RapidTransactions"] = (_, session) => session.TransactionCount > 10,
        ["UnusualLocation"] = (tx, session) =>
            !string.IsNullOrEmpty(session.BaseCountry) &&
            tx.CountryCode != session.BaseCountry,
        ["VeryHighAmount"] = (tx, _) => tx.Amount > 50000,
        ["LargeSessionTotal"] = (_, session) => session.TotalAmount > 100000,
    };

    public SimpleFraudRuleEngine(
        IOptionsMonitor<FraudEngineOptions> options,
        EventProcessorLog<SimpleFraudRuleEngine> log)
    {
        _options = options;
        _log = log;

        // Load rules initially
        ReloadRules();

        // Subscribe to configuration changes
        _options.OnChange(_ => ReloadRules());
    }

    public IReadOnlyList<RuleEvaluationResult> Evaluate(TransactionEvent transaction, FraudSession session)
    {
        var results = new List<RuleEvaluationResult>();

        List<LoadedRule> rulesToEvaluate;
        lock (_rulesLock)
        {
            rulesToEvaluate = _rules.ToList();
        }

        foreach (var rule in rulesToEvaluate)
        {
            if (!rule.Enabled || rule.CompiledPredicate == null)
                continue;

            try
            {
                var isMatch = rule.CompiledPredicate(transaction, session);

                results.Add(new RuleEvaluationResult
                {
                    RuleName = rule.Name,
                    IsMatch = isMatch,
                    ScoreModifier = isMatch ? rule.ScoreModifier : 0
                });
            }
            catch (Exception ex)
            {
                _log.Rules.Error.Eval.LogWarning(ex, "Rule '{RuleName}' evaluation failed", rule.Name);
                results.Add(new RuleEvaluationResult
                {
                    RuleName = rule.Name,
                    IsMatch = false,
                    ScoreModifier = 0,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    public IReadOnlyList<LoadedRule> GetRules()
    {
        lock (_rulesLock)
        {
            return _rules.ToList();
        }
    }

    public void ReloadRules()
    {
        var ruleOptions = _options.CurrentValue.Rules ?? new List<RuleOptions>();
        var newRules = new List<LoadedRule>();

        foreach (var opt in ruleOptions)
        {
            Func<TransactionEvent, FraudSession, bool>? predicate = null;
            string? compileError = null;

            if (opt.Enabled)
            {
                // Try to find a built-in rule implementation
                if (BuiltInRules.TryGetValue(opt.Name, out var builtIn))
                {
                    predicate = builtIn;
                }
                else
                {
                    // TODO: When JEX is available, compile the expression here
                    compileError = $"Rule '{opt.Name}' has no built-in implementation. JEX expressions not yet supported.";
                    _log.Rules.Error.Load.LogWarning("Rule '{RuleName}' cannot be compiled: JEX not available", opt.Name);
                }
            }

            newRules.Add(new LoadedRule
            {
                Name = opt.Name,
                Expression = opt.Expression,
                ScoreModifier = opt.ScoreModifier,
                Enabled = opt.Enabled,
                CompiledPredicate = predicate,
                CompileError = compileError
            });
        }

        lock (_rulesLock)
        {
            _rules = newRules;
        }

        _log.Rules.Loaded.LogInformation(
            "Loaded {TotalCount} rules, {EnabledCount} enabled, {CompiledCount} with implementations",
            newRules.Count,
            newRules.Count(r => r.Enabled),
            newRules.Count(r => r.CompiledPredicate != null));
    }
}

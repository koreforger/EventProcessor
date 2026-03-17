using EventProcessor.Models;
using EventProcessor.Services;
using KoreForge.Processing.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Workers.Pipeline;

/// <summary>
/// Third pipeline step: applies the loaded fraud rules to the extracted transaction fields
/// and produces a <see cref="FraudDecision"/>.
///
/// NOTE: This is a stub implementation. Full production logic requires FASTER session-state
/// lookup (load-or-create FraudSession per NID) which will be wired in once the session
/// bucket infrastructure is ready.
/// </summary>
internal sealed class FraudEvalStep : IPipelineStep<JObject, FraudDecision>
{
    private readonly IFraudRuleEngine _ruleEngine;
    private readonly ILogger<FraudEvalStep> _logger;

    public FraudEvalStep(IFraudRuleEngine ruleEngine, ILogger<FraudEvalStep> logger)
    {
        _ruleEngine = ruleEngine;
        _logger = logger;
    }

    public ValueTask<StepOutcome<FraudDecision>> InvokeAsync(
        JObject extracted,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        // Build a lightweight transaction object from the extracted fields.
        var tx = new TransactionEvent
        {
            NID = extracted.Value<string>("nid") ?? string.Empty,
            TransactionId = extracted.Value<string>("transactionId") ?? string.Empty,
            Amount = extracted.Value<decimal?>("amount") ?? 0m,
            CountryCode = extracted.Value<string>("countryCode"),
            Timestamp = DateTimeOffset.UtcNow,
        };

        // TODO: Load real session state from FASTER (session bucket lookup by NID hash).
        var session = new FraudSession { NID = tx.NID };
        var results = _ruleEngine.Evaluate(tx, session);

        var totalScore = results.Sum(r => r.ScoreModifier);
        var triggered = results.Where(r => r.IsMatch).Select(r => r.RuleName).ToList();

        var decisionType = totalScore >= 1.0
            ? DecisionType.Blocked
            : totalScore >= 0.5
                ? DecisionType.Flagged
                : DecisionType.Allow;

        if (decisionType != DecisionType.Allow)
        {
            _logger.LogInformation(
                "Fraud decision for NID {NID}: Decision={Decision}, Score={Score:F3}, Rules=[{Rules}]",
                tx.NID, decisionType, totalScore, string.Join(", ", triggered));
        }

        var decision = new FraudDecision
        {
            Score = totalScore,
            Decision = decisionType,
            DecidedAt = DateTimeOffset.UtcNow,
            TriggeredRuleNames = triggered,
        };

        return ValueTask.FromResult(StepOutcome<FraudDecision>.Continue(decision));
    }
}

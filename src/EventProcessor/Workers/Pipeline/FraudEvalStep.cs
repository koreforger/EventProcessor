using EventProcessor.Models;
using EventProcessor.Services;
using KoreForge.Processing.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Workers.Pipeline;

/// <summary>
/// Third pipeline step: loads (or creates) the FASTER-backed fraud session for the
/// incoming NID, applies built-in fraud rules, updates session state, persists back
/// to FASTER, and publishes the decision via the configured producer.
/// </summary>
internal sealed class FraudEvalStep : IPipelineStep<JObject, FraudDecision>
{
    private readonly IFraudRuleEngine _ruleEngine;
    private readonly ISessionStore _sessionStore;
    private readonly IFraudDecisionProducer _producer;
    private readonly ILogger<FraudEvalStep> _logger;

    public FraudEvalStep(
        IFraudRuleEngine ruleEngine,
        ISessionStore sessionStore,
        IFraudDecisionProducer producer,
        ILogger<FraudEvalStep> logger)
    {
        _ruleEngine = ruleEngine;
        _sessionStore = sessionStore;
        _producer = producer;
        _logger = logger;
    }

    public async ValueTask<StepOutcome<FraudDecision>> InvokeAsync(
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

        // 1. Load or create session from FASTER (bucket-routed by NID hash).
        //    On cache miss, loads from SQL (cache-through).
        var session = await _sessionStore.GetOrCreateAsync(tx.NID);

        // 2. Accumulate transaction into session state.
        session.TransactionCount++;
        session.TotalAmount += tx.Amount;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        session.BaseCountry ??= tx.CountryCode;
        session.EarliestTransactionAt ??= tx.Timestamp;

        session.Transactions.Add(new TransactionRecord
        {
            TransactionId = tx.TransactionId,
            Amount = tx.Amount,
            Timestamp = tx.Timestamp,
            CountryCode = tx.CountryCode,
        });

        // 3. Evaluate fraud rules against current transaction + session state.
        var results = _ruleEngine.Evaluate(tx, session);
        var totalScore = results.Sum(r => r.ScoreModifier);
        var triggered = results.Where(r => r.IsMatch).Select(r => r.RuleName).ToList();

        var decisionType = totalScore >= 1.0
            ? DecisionType.Blocked
            : totalScore >= 0.5
                ? DecisionType.Flagged
                : DecisionType.Allow;

        // 4. Update session score and triggered rules, persist to FASTER.
        session.CurrentScore = totalScore;
        session.TriggeredRules = results
            .Where(r => r.IsMatch)
            .Select(r => new RuleResult
            {
                RuleName = r.RuleName,
                ScoreAdjustment = r.ScoreModifier,
                MatchedAt = DateTimeOffset.UtcNow,
            })
            .ToList();

        var decision = new FraudDecision
        {
            Score = totalScore,
            Decision = decisionType,
            DecidedAt = DateTimeOffset.UtcNow,
            TriggeredRuleNames = triggered,
        };
        session.Decision = decision;

        _sessionStore.Put(tx.NID, session);

        // 5. Publish decision to Kafka (no-op if producer disabled).
        await _producer.ProduceAsync(tx.NID, decision, cancellationToken);

        if (decisionType != DecisionType.Allow)
        {
            _logger.LogInformation(
                "Fraud decision for NID {NID}: Decision={Decision}, Score={Score:F3}, Rules=[{Rules}]",
                tx.NID, decisionType, totalScore, string.Join(", ", triggered));
        }

        return StepOutcome<FraudDecision>.Continue(decision);
    }
}

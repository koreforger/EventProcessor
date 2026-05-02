using System.Text.Json;
using EventProcessor.Logging;
using EventProcessor.Monitoring;
using EventProcessor.Rules;
using EventProcessor.Session;
using KoreForge.Kafka.Consumer.Abstractions;
using KoreForge.Kafka.Consumer.Batch;
using Event.Streaming.Processing.Monitoring;
using KoreForge.Time;

namespace EventProcessor.Kafka;

public sealed class EventProcessorKafkaBatchProcessor : IKafkaBatchProcessor
{
    private readonly EventProcessorLogger<EventProcessorKafkaBatchProcessor> _log;
    private readonly FraudSessionStore _sessions;
    private readonly RuleEvaluator _rules;
    private readonly EventProcessorMetricsAccumulator _metrics;
    private readonly EventProcessorMonitoringSnapshot _snapshot;
    private readonly IIncidentStore _incidents;
    private readonly ISystemClock _clock;

    public EventProcessorKafkaBatchProcessor(
        EventProcessorLogger<EventProcessorKafkaBatchProcessor> log,
        FraudSessionStore sessions,
        RuleEvaluator rules,
        EventProcessorMetricsAccumulator metrics,
        EventProcessorMonitoringSnapshot snapshot,
        IIncidentStore incidents,
        ISystemClock clock)
    {
        _log = log;
        _sessions = sessions;
        _rules = rules;
        _metrics = metrics;
        _snapshot = snapshot;
        _incidents = incidents;
        _clock = clock;
    }

    public Task ProcessAsync(KafkaRecordBatch batch, CancellationToken cancellationToken)
    {
        _log.Kafka.Batch.Received.LogInformation("EventProcessor received batch with {Count} records", batch.Count);

        foreach (var record in batch.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var document = JsonDocument.Parse(record.Message.Value);
                var root = document.RootElement;

                var entityId = root.TryGetProperty("entityId", out var entity)
                    ? entity.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(entityId))
                {
                    throw new JsonException("Payload is missing required property 'entityId'.");
                }

                var amount = root.TryGetProperty("amount", out var amountElement)
                    ? amountElement.GetDecimal()
                    : 0m;

                var session = _sessions.GetOrCreate(entityId);
                lock (session)
                {
                    session.EventCount++;
                    session.TotalAmount += amount;
                    session.LastActivityAt = _clock.UtcNow;

                    var decision = _rules.Evaluate(session);
                    session.LastDecision = decision.Outcome.ToString();
                    foreach (var signal in decision.Signals)
                    {
                        session.RiskSignals.Add(signal);
                    }

                    _metrics.RecordProcessed();
                    _metrics.RecordDecision();

                    if (decision.Outcome == DecisionOutcome.Blocked)
                    {
                        _log.Decision.Blocked.LogWarning(
                            "EventProcessor blocked entity {EntityId} after {EventCount} events and total amount {TotalAmount}",
                            entityId, session.EventCount, session.TotalAmount);
                    }
                    else if (decision.Outcome == DecisionOutcome.Flagged)
                    {
                        _log.Decision.Flagged.LogInformation(
                            "EventProcessor flagged entity {EntityId} after {EventCount} events and total amount {TotalAmount}",
                            entityId, session.EventCount, session.TotalAmount);
                    }
                    else
                    {
                        _log.Decision.Approved.LogInformation(
                            "EventProcessor approved entity {EntityId} after {EventCount} events and total amount {TotalAmount}",
                            entityId, session.EventCount, session.TotalAmount);
                    }
                }
            }
            catch (JsonException ex)
            {
                _metrics.RecordError();
                _incidents.Record(new OperationalIncident
                {
                    Application = "EventProcessor",
                    InstanceId = _snapshot.Instance,
                    Category = "Rules",
                    Severity = "Error",
                    Message = "Failed to parse EventProcessor payload",
                    Detail = ex.Message,
                    OccurredAt = _clock.UtcNow,
                });
                _log.Kafka.Consumer.Error.LogError(
                    ex,
                    "EventProcessor failed to process payload from topic {Topic} partition {Partition} offset {Offset}",
                    record.Topic, record.Partition.Value, record.Offset.Value);
            }
        }

        _snapshot.Timestamp = _clock.UtcNow;
        _snapshot.Health = _metrics.TotalErrors == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        _snapshot.ActiveSessionCount = _sessions.ActiveCount;
        _snapshot.TotalDecisions = _metrics.TotalDecisions;
        _snapshot.PipelineMetrics = new PipelineMetrics
        {
            TotalProcessed = _metrics.TotalProcessed,
            TotalErrors = _metrics.TotalErrors,
        };
        _snapshot.AppSpecificMetrics = new Dictionary<string, object>
        {
            ["ActiveSessions"] = _sessions.ActiveCount,
            ["TotalDecisions"] = _metrics.TotalDecisions,
            ["Errors"] = _metrics.TotalErrors,
        };

        return Task.CompletedTask;
    }
}
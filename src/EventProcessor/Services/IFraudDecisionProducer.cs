using EventProcessor.Models;

namespace EventProcessor.Services;

/// <summary>
/// Publishes fraud decisions to a Kafka topic.
/// Implementations may be no-op (disabled) or backed by Confluent.Kafka.
/// </summary>
public interface IFraudDecisionProducer : IDisposable
{
    Task ProduceAsync(string nid, FraudDecision decision, CancellationToken ct = default);
}

using EventProcessor.Models;

namespace EventProcessor.Services;

/// <summary>
/// No-op producer used when the Kafka decision producer is disabled.
/// </summary>
internal sealed class NoOpFraudDecisionProducer : IFraudDecisionProducer
{
    public Task ProduceAsync(string nid, FraudDecision decision, CancellationToken ct) =>
        Task.CompletedTask;

    public void Dispose() { }
}

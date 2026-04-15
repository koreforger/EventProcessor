using EventProcessor.Models;
using EventProcessor.Services;
using FluentAssertions;

namespace EventProcessor.Tests;

public sealed class NoOpFraudDecisionProducerTests
{
    [Fact]
    public async Task ProduceAsync_completes_without_error()
    {
        using var producer = new NoOpFraudDecisionProducer();
        var decision = new FraudDecision
        {
            Score = 0.5,
            Decision = DecisionType.Flagged,
            DecidedAt = DateTimeOffset.UtcNow,
        };

        var act = () => producer.ProduceAsync("NID-001", decision, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var producer = new NoOpFraudDecisionProducer();
        producer.Dispose();
        producer.Dispose(); // should not throw
    }
}

using EventProcessor.HealthChecks;
using FluentAssertions;

namespace EventProcessor.Tests.HealthChecks;

public sealed class KafkaConsumerStateTests
{
    [Fact]
    public void Default_state_is_starting()
    {
        var state = new KafkaConsumerState();

        state.Status.Should().Be(KafkaConsumerHealthStatus.Starting);
        state.Detail.Should().BeNull();
    }

    [Fact]
    public void ReportRunning_sets_status_and_clears_detail()
    {
        var state = new KafkaConsumerState();
        state.ReportFaulted("some error"); // set detail first

        state.ReportRunning();

        state.Status.Should().Be(KafkaConsumerHealthStatus.Running);
        state.Detail.Should().BeNull();
    }

    [Fact]
    public void ReportFaulted_sets_status_and_detail()
    {
        var state = new KafkaConsumerState();

        state.ReportFaulted("Connection lost");

        state.Status.Should().Be(KafkaConsumerHealthStatus.Faulted);
        state.Detail.Should().Be("Connection lost");
    }

    [Fact]
    public void ReportStopped_sets_status_and_clears_detail()
    {
        var state = new KafkaConsumerState();
        state.ReportFaulted("some error");

        state.ReportStopped();

        state.Status.Should().Be(KafkaConsumerHealthStatus.Stopped);
        state.Detail.Should().BeNull();
    }

    [Fact]
    public void State_transitions_running_to_faulted_to_running()
    {
        var state = new KafkaConsumerState();

        state.ReportRunning();
        state.Status.Should().Be(KafkaConsumerHealthStatus.Running);

        state.ReportFaulted("Kafka broker unavailable");
        state.Status.Should().Be(KafkaConsumerHealthStatus.Faulted);
        state.Detail.Should().Be("Kafka broker unavailable");

        state.ReportRunning();
        state.Status.Should().Be(KafkaConsumerHealthStatus.Running);
        state.Detail.Should().BeNull();
    }
}

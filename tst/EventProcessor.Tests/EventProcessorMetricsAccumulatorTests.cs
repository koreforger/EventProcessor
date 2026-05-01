using EventProcessor.Monitoring;

namespace EventProcessor.Tests;

public sealed class EventProcessorMetricsAccumulatorTests
{
    [Fact]
    public void InitialCountsAreZero()
    {
        var acc = new EventProcessorMetricsAccumulator();
        Assert.Equal(0, acc.TotalProcessed);
        Assert.Equal(0, acc.TotalErrors);
        Assert.Equal(0, acc.StopBoundarySkipped);
        Assert.Equal(0, acc.TotalDecisions);
    }

    [Fact]
    public void RecordProcessed_IncrementsProcessed()
    {
        var acc = new EventProcessorMetricsAccumulator();
        acc.RecordProcessed();
        acc.RecordProcessed();
        Assert.Equal(2, acc.TotalProcessed);
    }

    [Fact]
    public void RecordDecision_IncrementsDecisions()
    {
        var acc = new EventProcessorMetricsAccumulator();
        acc.RecordDecision();
        Assert.Equal(1, acc.TotalDecisions);
    }

    [Fact]
    public void IndependentCounters_DoNotInterfere()
    {
        var acc = new EventProcessorMetricsAccumulator();
        acc.RecordProcessed();
        acc.RecordError();
        acc.RecordStopBoundarySkip();
        acc.RecordDecision();
        Assert.Equal(1, acc.TotalProcessed);
        Assert.Equal(1, acc.TotalErrors);
        Assert.Equal(1, acc.StopBoundarySkipped);
        Assert.Equal(1, acc.TotalDecisions);
    }
}

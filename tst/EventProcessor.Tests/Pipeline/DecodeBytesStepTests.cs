using Confluent.Kafka;
using EventProcessor.Workers.Pipeline;
using KF.Kafka.Consumer.Pipelines;
using KoreForge.Processing.Pipelines;
using static KoreForge.Processing.Pipelines.StepOutcomeKind;

namespace EventProcessor.Tests;

public sealed class DecodeBytesStepTests
{
    private static KafkaPipelineRecord MakeRecord(byte[] value)
    {
        var msg = new Message<byte[], byte[]> { Value = value };
        var result = new ConsumeResult<byte[], byte[]>
        {
            Topic = "test",
            Message = msg,
        };
        return new KafkaPipelineRecord(result);
    }

    private readonly DecodeBytesStep _step = new();

    [Fact]
    public async Task Empty_payload_aborts()
    {
        var record = MakeRecord([]);
        var outcome = await _step.InvokeAsync(record, new PipelineContext(), default);
        Assert.Equal(Abort, outcome.Kind);
    }

    [Fact]
    public async Task Valid_utf8_payload_continues_with_string()
    {
        var record = MakeRecord("{\"nid\":\"ABC123\"}"u8.ToArray());
        var outcome = await _step.InvokeAsync(record, new PipelineContext(), default);
        Assert.Equal(Continue, outcome.Kind);
        Assert.Equal("{\"nid\":\"ABC123\"}", outcome.Value);
    }
}

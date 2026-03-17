using System.Text;
using KF.Kafka.Consumer.Pipelines;
using KoreForge.Processing.Pipelines;

namespace EventProcessor.Workers.Pipeline;

/// <summary>
/// First pipeline step: decodes the raw Kafka message bytes to a UTF-8 JSON string.
/// Aborts the record if the payload is empty.
/// </summary>
internal sealed class DecodeBytesStep : IPipelineStep<KafkaPipelineRecord, string>
{
    public ValueTask<StepOutcome<string>> InvokeAsync(
        KafkaPipelineRecord record,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (record.Value.IsEmpty)
            return ValueTask.FromResult(StepOutcome<string>.Abort());

        var json = Encoding.UTF8.GetString(record.Value.Span);
        return ValueTask.FromResult(StepOutcome<string>.Continue(json));
    }
}

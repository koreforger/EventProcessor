using EventProcessor.Services;
using KoreForge.Processing.Pipelines;
using Newtonsoft.Json.Linq;

namespace EventProcessor.Workers.Pipeline;

/// <summary>
/// Second pipeline step: runs the JEX field extraction script against the raw JSON string.
/// Logging is handled inside <see cref="JexFieldExtractorService.Extract"/>.
/// Aborts the record when extraction returns null.
/// </summary>
internal sealed class JexExtractStep : IPipelineStep<string, JObject>
{
    private readonly JexFieldExtractorService _extractor;

    public JexExtractStep(JexFieldExtractorService extractor) => _extractor = extractor;

    public ValueTask<StepOutcome<JObject>> InvokeAsync(
        string json,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var result = _extractor.Extract(json);
        return result is null
            ? ValueTask.FromResult(StepOutcome<JObject>.Abort())
            : ValueTask.FromResult(StepOutcome<JObject>.Continue(result));
    }
}
